"""Contrato producido por la serialización real de los modelos del lanzador."""
import json
import subprocess
import tempfile
import unittest
from datetime import date
from pathlib import Path

from training_analysis import normalise_journal, activity_catalog_document

ROOT = Path(__file__).resolve().parents[1]


class LauncherContractTests(unittest.TestCase):
    def test_real_python_catalog_is_accepted_by_launcher(self):
        document = activity_catalog_document([{"activityId": 123456789, "activityName": "Rodaje ficticio",
            "startTimeLocal": "2026-01-10 10:00:00", "distance": 10000, "duration": 3600}], b"synthetic-secret", date(2026,1,1), date(2026,1,31))
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "catalog.json"
            path.write_text(json.dumps(document), encoding="utf-8")
            result = subprocess.run(["dotnet", "run", "--project", str(ROOT / "GarminDataExport.Tests"),
                "--no-build", "--", "--validate-catalog", str(path)], cwd=ROOT,
                capture_output=True, text=True, encoding="utf-8", timeout=60)
            self.assertEqual(0, result.returncode, result.stdout + result.stderr)

    def test_real_csharp_journal_is_accepted_without_changing_consent(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "journal.json"
            result = subprocess.run(["dotnet", "run", "--project", str(ROOT / "GarminDataExport.Tests"),
                "--no-build", "--", "--emit-contract", str(path)], cwd=ROOT,
                capture_output=True, text=True, encoding="utf-8", timeout=60)
            self.assertEqual(0, result.returncode, result.stdout + result.stderr)
            journal = normalise_journal(json.loads(path.read_text(encoding="utf-8")))
        self.assertNotIn("activity_ref", journal[0])
        self.assertEqual(0, journal[0]["pain_0_10"])
        self.assertNotIn("user_rpe_1_10", journal[0])
        self.assertEqual("Descanso ficticio", journal[0]["note"])
        self.assertNotIn("note", journal[1])
        self.assertEqual("activity_012345abcdef", journal[1]["activity_ref"])
