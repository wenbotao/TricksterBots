"""Preserve test evidence and independently replay all 540 saved match records."""
from pathlib import Path
import gzip
import hashlib
import json
import shutil
import subprocess
import sys
import tempfile
import xml.etree.ElementTree as ET

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[1]
DATA = HERE / "results"
trx, audit = map(Path, sys.argv[1:3])
ns = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
document = ET.parse(trx)
counters = document.find(".//t:Counters", ns).attrib
assert counters["failed"] == "0" and counters["passed"] == counters["total"]
outcomes = [{"Name": row.attrib["testName"], "Outcome": row.attrib["outcome"], "Duration": row.attrib.get("duration")}
            for row in document.findall(".//t:UnitTestResult", ns)]
new_tests = {row.attrib["id"] for row in document.findall(".//t:UnitTest", ns)
             if row.find("t:TestMethod", ns).attrib["className"].split(",")[0] == "TestBots.TestV11Compliance"}
new_outcomes = [{"Name": row.attrib["testName"], "Outcome": row.attrib["outcome"], "Duration": row.attrib.get("duration")}
                for row in document.findall(".//t:UnitTestResult", ns) if row.attrib["testId"] in new_tests]
assert len(new_outcomes) == 55 and all(row["Outcome"] == "Passed" for row in new_outcomes)
proof = {"Counters": counters, "NewComplianceTests": new_outcomes, "AllResults": outcomes,
         "TrxSha256": hashlib.sha256(trx.read_bytes()).hexdigest(),
         "TestSourceSha256": hashlib.sha256((ROOT / "TestBots/TestV11Compliance.cs").read_bytes()).hexdigest()}
(DATA / "test-results.json").write_text(json.dumps(proof, ensure_ascii=False, indent=2) + "\n")
shutil.copyfile(audit, DATA / "compliance-audit.jsonl")
if "--tests-only" in sys.argv[3:]:
    print(json.dumps({"Counters": counters, "NewComplianceTests": len(new_outcomes)}, indent=2))
    sys.exit(0)
plans = json.loads((DATA / "plan.json").read_text())
assert len(plans) == 540 and not list(DATA.glob("*.failure.json"))
with tempfile.NamedTemporaryFile(mode="wb", prefix="fivehundred-v11-independent-", suffix=".jsonl", delete=False) as temp:
    replay_input = Path(temp.name)
    for i, plan in enumerate(plans):
        summary = json.loads((DATA / f"game-{i:03d}.json").read_text())
        assert summary["Plan"] == plan and summary["Winner"] in (0, 1) and not summary["Truncated"]
        raw = gzip.decompress((DATA / f"game-{i:03d}.game.gz").read_bytes())
        temp.write(raw.rstrip(b"\n") + b"\n")
try:
    result = subprocess.run(["dotnet", str(ROOT / "FiveHundredArena/bin/Release/net8.0/FiveHundredArena.dll"), "replay", str(replay_input)],
                            cwd=ROOT, capture_output=True, text=True, check=True)
    assert "Validated 540 games" in result.stdout
    evidence = {"Validated": 540, "ExitCode": result.returncode, "Stdout": result.stdout, "Stderr": result.stderr,
                "ReplayInputSha256": hashlib.sha256(replay_input.read_bytes()).hexdigest(),
                "Method": "Separate FiveHundredArena CLI process replays every saved decision; no strategy search or outcome filtering."}
    (DATA / "independent-replay.json").write_text(json.dumps(evidence, indent=2) + "\n")
    print(result.stdout)
finally:
    replay_input.unlink()
