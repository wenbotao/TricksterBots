using Trickster.cloud;

namespace Trickster.Bots
{
    /// <summary>The original FiveHundred strategy, exposed under its benchmark difficulty name.</summary>
    public sealed class MediumBot : FiveHundredBot
    {
        public MediumBot(FiveHundredOptions options, Suit trumpSuit) : base(options, trumpSuit) { }
    }
}
