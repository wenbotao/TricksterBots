using System.Security.Cryptography;
using System.Text.Json;
using FiveHundredArena;
using Trickster.cloud;
namespace FiveHundredLab;

public static class FeaturesV1
{
    public const string Schema = "public-observation-features-v1";
    public static double[] Extract(Observation v, string phase, Contract? proposed = null)
    {
        var c = proposed ?? v.Contract ?? new Contract(6, Suit.Unknown);
        bool ourContract = (proposed != null ? v.Seat : v.Declarer ?? -1) % 2 == v.Seat % 2;
        var f = new CardFrameV1(v with { Contract = c });
        int taken = v.Players.Sum(p => p.Tricks), teamTaken = v.Players.Where(p => p.Seat % 2 == v.Seat % 2).Sum(p => p.Tricks);
        var result = new List<double>
        {
            phase == "bid" ? 1 : 0, phase == "discard" ? 1 : 0, phase == "play" ? 1 : 0,
            c.IsMisere ? 1 : 0, c.Kind == ContractKind.OpenMisere ? 1 : 0, c.Trump == Suit.Unknown ? 1 : 0,
            ourContract ? 1 : 0, v.Seat == v.Declarer ? 1 : 0,
            v.Hand.Count / 13.0, taken / 10.0, teamTaken / 10.0, c.Tricks / 10.0, v.Rules.Value(c) / 520.0,
            v.Scores[v.Seat % 2] / 500.0, v.Scores[1-v.Seat%2] / 500.0,
            v.Trick.Count / 4.0, v.Hand.Any(x => x.IsJoker) ? 1 : 0,
            LegacyFeaturesV1.HandValue(v.Hand, c, v.Rules) / 10.0,
            TacticianV1.Retained(v.Hand, c, v.Rules) / 10.0,
            v.Hand.Count(x => f.Boss(x)) / 10.0,
            v.Hand.Count(x => x.Rank == 14) / 4.0,
            v.Players.Sum(p => p.ExposedHand?.Count ?? 0) / 10.0,
            v.OwnDiscards.Count / 3.0,
            v.Auction.Count(a => a.Bid != null && a.Seat % 2 == v.Seat % 2) / 4.0,
            v.Auction.Count(a => a.Bid != null && a.Seat % 2 != v.Seat % 2) / 4.0
        };
        foreach (var suit in Rules.Suits)
        {
            int length = f.Length[(int)suit];
            result.Add(length / 10.0);
            result.Add(v.Hand.Where(x => f.Table.Suit(x) == (int)suit).Select(x => f.Table.Strength(x) / 100.0).DefaultIfEmpty(0).Max());
            result.Add(Enumerable.Range(0, 4).Count(s => s % 2 != v.Seat % 2 && f.IsVoid(s, (int)suit)) / 2.0);
        }
        double strength = LegacyFeaturesV1.HandValue(v.Hand, c, v.Rules) / 10;
        result.Add(strength * (10-taken)/10.0); result.Add(strength * (ourContract ? 1 : -1));
        result.Add((c.Tricks - (ourContract ? teamTaken : taken-teamTaken)) / 10.0);
        result.Add((v.Scores[v.Seat%2] + v.Rules.Value(c) >= v.Rules.WinScore) ? 1 : 0);
        result.Add(proposed == null ? 0 : 1); // bid action channel; null means Pass when phase=bid
        return result.ToArray();
    }
}
public sealed record TrainingRowV1(int GroupSeed, string Phase, double[] Features, double Utility, double MadeProbability, double DeclarerTricks);
public sealed class ValueModelV1
{
    public string ModelId { get; set; } = "";
    public string FeatureSchema { get; set; } = FeaturesV1.Schema;
    public string RuleFingerprint { get; set; } = "";
    public string Teacher { get; set; } = "belief-full16-pool-v1";
    public int TeacherSamples { get; set; } = 16;
    public int[] TrainingSeeds { get; set; } = [];
    public int[] ValidationSeeds { get; set; } = [];
    public double[] Mean { get; set; } = [];
    public double[] Scale { get; set; } = [];
    public double[][] Weights { get; set; } = [];
    public Dictionary<string, ValueModelV1> PhaseModels { get; set; } = [];
    public Dictionary<string, double> Metrics { get; set; } = [];
    [System.Text.Json.Serialization.JsonIgnore] public string Digest { get; private set; } = "unsaved";
    public static string Fingerprint(Rules rules) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(rules, LabJson.Options)));
    public static ValueModelV1 Load(string path)
    {
        var bytes = File.ReadAllBytes(path); var m = JsonSerializer.Deserialize<ValueModelV1>(bytes, LabJson.Options) ?? throw new InvalidDataException("Empty model");
        m.Validate(); m.Digest = Convert.ToHexString(SHA256.HashData(bytes))[..16].ToLowerInvariant(); return m;
    }
    public void Validate()
    {
        int dimension = FeaturesV1.Extract(new(0, 3, 0, [], Enumerable.Range(0,4).Select(s=>new PublicPlayer(s,0,0,[],0,[],null)).ToArray(), [], new(6,Suit.Unknown),0,[],[],[0,0],new(),[]),"play").Length;
        if (FeatureSchema != FeaturesV1.Schema || string.IsNullOrWhiteSpace(ModelId) || RuleFingerprint != Fingerprint(new Rules()) || TeacherSamples < 1 || Mean.Length != dimension || Scale.Length != dimension || Scale.Any(x => !double.IsFinite(x) || x <= 0) || Mean.Any(x=>!double.IsFinite(x)) || Weights.Length != 3 || Weights.Any(w => w.Length != dimension+1 || w.Any(x => !double.IsFinite(x))) || TrainingSeeds.Intersect(ValidationSeeds).Any()) throw new InvalidDataException("Invalid/incompatible value model or leaking group split");
        if(PhaseModels.Count>3 || PhaseModels.Keys.Any(k=>k!="bid"&&k!="play"&&k!="discard"))throw new InvalidDataException("Invalid phase heads");
        foreach(var child in PhaseModels.Values)
        {
            if(child.PhaseModels.Count!=0 || child.TrainingSeeds.Except(TrainingSeeds).Any() || child.ValidationSeeds.Except(TrainingSeeds).Any())throw new InvalidDataException("Leaking phase head split");
            child.Validate();
        }
    }
    public RolloutValueV1 Predict(Observation v, string phase, Contract? proposed = null)
    {
        if (Fingerprint(v.Rules) != RuleFingerprint) throw new NotSupportedException("Model rule profile mismatch");
        var values = PredictRaw(FeaturesV1.Extract(v, phase, proposed), phase);
        return new(Math.Clamp(values[0]*1000,-4*v.Rules.WinScore,4*v.Rules.WinScore), Math.Clamp(values[1],0,1), Math.Clamp(values[2]*10,0,10),false);
    }
    private double[] PredictRaw(double[] features,string phase)
    {
        if(PhaseModels.TryGetValue(phase,out var child))return child.PredictRaw(features,phase);
        var x=Standardize(features);
        return Enumerable.Range(0,3).Select(h=>Weights[h][0]+Enumerable.Range(0,x.Length).Sum(i=>x[i]*Weights[h][i+1])).ToArray();
    }
    public static ValueModelV1 FitByPhaseV2(string id,IReadOnlyList<TrainingRowV1> rows)
    {
        var parent=Fit(id,rows);
        foreach(var group in rows.Where(r=>parent.TrainingSeeds.Contains(r.GroupSeed)).GroupBy(r=>r.Phase))
            if(group.Select(r=>r.GroupSeed).Distinct().Count()>=5)parent.PhaseModels[group.Key]=Fit(id+"-"+group.Key,group.ToArray());
        var valid=rows.Where(r=>parent.ValidationSeeds.Contains(r.GroupSeed)).ToArray();
        parent.Metrics["validationPhaseUtilityRMSE"]=Math.Sqrt(valid.Average(r=>Math.Pow(parent.PredictRaw(r.Features,r.Phase)[0]*1000-r.Utility,2)));
        parent.Metrics["validationPhaseTeacherProbabilityRMSE"]=Math.Sqrt(valid.Where(r=>r.Phase!="bid").Average(r=>Math.Pow(Math.Clamp(parent.PredictRaw(r.Features,r.Phase)[1],0,1)-r.MadeProbability,2)));
        parent.Validate();return parent;
    }
    private double[] Standardize(double[] f) => f.Select((x,i)=>Math.Clamp((x-Mean[i])/Scale[i],-6,6)).ToArray();
    public static ValueModelV1 Fit(string id, IReadOnlyList<TrainingRowV1> rows)
    {
        var groups=rows.Select(r=>r.GroupSeed).Distinct().OrderBy(x=>x).ToArray();
        if(groups.Length<5)throw new ArgumentException("At least five independent seed groups required");
        var validation=groups.Where((_,i)=>i%5==0).ToArray(); var train=rows.Where(r=>!validation.Contains(r.GroupSeed)).ToArray(); var valid=rows.Where(r=>validation.Contains(r.GroupSeed)).ToArray();
        int d=train[0].Features.Length; var model=new ValueModelV1{ModelId=id,RuleFingerprint=Fingerprint(new()),TrainingSeeds=groups.Except(validation).ToArray(),ValidationSeeds=validation};
        model.Mean=Enumerable.Range(0,d).Select(i=>train.Average(r=>r.Features[i])).ToArray();
        model.Scale=Enumerable.Range(0,d).Select(i=>Math.Max(.05,Math.Sqrt(train.Average(r=>Math.Pow(r.Features[i]-model.Mean[i],2))))).ToArray();
        var matrix=new double[d+1,d+1]; var rhs=new double[3,d+1];
        foreach(var row in train)
        {
            var x=new[]{1.0}.Concat(model.Standardize(row.Features)).ToArray(); double[] y=[row.Utility/1000,row.MadeProbability,row.DeclarerTricks/10];
            for(int i=0;i<=d;i++){for(int j=0;j<=d;j++)matrix[i,j]+=x[i]*x[j]/train.Length; for(int h=0;h<3;h++)rhs[h,i]+=x[i]*y[h]/train.Length;}
        }
        for(int i=1;i<=d;i++)matrix[i,i]+=.04;
        model.Weights=Enumerable.Range(0,3).Select(h=>Solve(matrix,Enumerable.Range(0,d+1).Select(i=>rhs[h,i]).ToArray())).ToArray();
        foreach(var subset in new[]{("train",train),("validation",valid)})
        {
            double error=0, probability=0;
            foreach(var row in subset.Item2){var x=model.Standardize(row.Features);double Head(int h)=>model.Weights[h][0]+Enumerable.Range(0,d).Sum(i=>x[i]*model.Weights[h][i+1]);error+=Math.Pow(Head(0)*1000-row.Utility,2);probability+=Math.Pow(Math.Clamp(Head(1),0,1)-row.MadeProbability,2);}
            model.Metrics[subset.Item1+"Rows"]=subset.Item2.Length;model.Metrics[subset.Item1+"UtilityRMSE"]=Math.Sqrt(error/subset.Item2.Length);model.Metrics[subset.Item1+"TeacherProbabilityRMSE"]=Math.Sqrt(probability/subset.Item2.Length);
        }
        model.Validate(); return model;
    }
    private static double[] Solve(double[,] matrix,double[] b)
    {
        int n=b.Length;var a=(double[,])matrix.Clone();b=b.ToArray();
        for(int k=0;k<n;k++)
        {
            int pivot=k;for(int i=k+1;i<n;i++)if(Math.Abs(a[i,k])>Math.Abs(a[pivot,k]))pivot=i;
            if(Math.Abs(a[pivot,k])<1e-10)throw new InvalidDataException("Singular ridge system");
            if(pivot!=k){for(int j=k;j<n;j++)(a[k,j],a[pivot,j])=(a[pivot,j],a[k,j]);(b[k],b[pivot])=(b[pivot],b[k]);}
            for(int i=k+1;i<n;i++){double factor=a[i,k]/a[k,k];for(int j=k;j<n;j++)a[i,j]-=factor*a[k,j];b[i]-=factor*b[k];}
        }
        var x=new double[n];for(int i=n-1;i>=0;i--){double v=b[i];for(int j=i+1;j<n;j++)v-=a[i,j]*x[j];x[i]=v/a[i,i];}return x;
    }
}
