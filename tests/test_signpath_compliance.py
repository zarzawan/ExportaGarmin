import re
import unittest
import xml.etree.ElementTree as ET
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]


class SignPathPreparationTests(unittest.TestCase):
    def test_public_policies_and_required_statement_exist(self):
        signing = (ROOT / "CODE_SIGNING.md").read_text(encoding="utf-8")
        privacy = (ROOT / "PRIVACY.md").read_text(encoding="utf-8")
        uninstall = (ROOT / "UNINSTALL.md").read_text(encoding="utf-8")

        self.assertIn("# Code signing policy", signing)
        self.assertIn("Free code signing provided by", signing)
        self.assertIn("certificate by", signing)
        self.assertIn("solicitud", signing.casefold())
        self.assertIn("will not transfer any information", privacy)
        self.assertIn("%LOCALAPPDATA%", uninstall)
        self.assertIn("%USERPROFILE%", uninstall)

    def test_artifact_configuration_signs_only_the_owned_launcher(self):
        path = ROOT / ".signpath" / "artifact-configuration.xml"
        root = ET.parse(path).getroot()
        namespace = {"s": "http://signpath.io/artifact-configuration/v1"}
        pe_files = root.findall(".//s:pe-file", namespace)

        self.assertEqual(1, len(pe_files))
        launcher = pe_files[0]
        self.assertEqual("ExportaGarmin.exe", launcher.attrib["path"])
        self.assertEqual("ExportaGarmin", launcher.attrib["product-name"])
        self.assertEqual("zarzawan", launcher.attrib["company-name"])
        signatures = launcher.findall("s:authenticode-sign", namespace)
        self.assertEqual(1, len(signatures))
        self.assertEqual("sha256", signatures[0].attrib["hash-algorithm"])

    def test_launcher_metadata_is_explicit_and_versioned(self):
        project = ET.parse(
            ROOT
            / "GarminDataExport.Launcher"
            / "GarminDataExport.Launcher.csproj"
        ).getroot()

        values = {
            child.tag: (child.text or "").strip()
            for group in project.findall("PropertyGroup")
            for child in group
        }
        self.assertEqual("ExportaGarmin", values["Product"])
        self.assertEqual("ExportaGarmin", values["AssemblyTitle"])
        self.assertEqual("zarzawan", values["Company"])
        self.assertEqual("$(Version)", values["InformationalVersion"])
        self.assertEqual("10.0.10", values["RuntimeFrameworkVersion"])
        self.assertEqual("Apache-2.0", values["PackageLicenseExpression"])

    def test_workflows_use_github_hosted_runner_and_pinned_actions(self):
        for filename in ("ci.yml", "release.yml"):
            workflow = (
                ROOT / ".github" / "workflows" / filename
            ).read_text(encoding="utf-8")
            self.assertIn("runs-on: windows-latest", workflow)
            self.assertNotIn("self-hosted", workflow)
            uses = re.findall(r"uses:\s+([^\s#]+)", workflow)
            self.assertTrue(uses)
            for action in uses:
                self.assertRegex(action, r"@[0-9a-f]{40}$")

    def test_portable_builder_includes_policies_and_dotnet_notices(self):
        builder = (
            ROOT / "scripts" / "Build-PortableRelease.ps1"
        ).read_text(encoding="utf-8")
        for required in (
            "CODE_SIGNING.md",
            "PRIVACY.md",
            "UNINSTALL.md",
            "DOTNET_LICENSE.txt",
            "DOTNET_THIRD_PARTY_NOTICES.txt",
            "10.0.10",
        ):
            self.assertIn(required, builder)


if __name__ == "__main__":
    unittest.main()
