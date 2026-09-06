"""Regresiones con datos sintéticos y el transporte de la dependencia instalada."""
import io
import json
import logging
import os
import subprocess
import sys
import tempfile
import unittest
from datetime import date
from pathlib import Path
from types import SimpleNamespace
from unittest.mock import Mock, patch

import requests
import garmin_export as ge
import training_analysis as ta
from export_events import EventEmitter
from garmin_transport import configure_private_logging, regulate_transport
from profile_binding import ProfileBindingError, verify_account


def response(status, data):
    result = requests.Response()
    result.status_code = status
    result._content = json.dumps(data).encode()
    result._content_consumed = True
    result.url = "https://example.invalid/activities"
    return result


class TransportTests(unittest.TestCase):
    def setUp(self):
        self.limiter = Mock()
        self.api = object.__new__(ge.Garmin)
        self.api.garmin_connect_activities = "/activities"
        self.session = requests.Session()
        self.send = Mock()
        self.http_patch = patch("requests.adapters.HTTPAdapter.send", self.send)
        self.http_patch.start()
        self.api.garth = SimpleNamespace(sess=self.session, telemetry=None,
            connectapi=lambda path, **kwargs: self.get(path, **kwargs))
        regulate_transport(self.api, lambda: self.limiter)

    def tearDown(self):
        self.session.close()
        self.http_patch.stop()

    def get(self, path, **kwargs):
        result = self.session.get("https://example.invalid" + path, **kwargs)
        result.raise_for_status()
        return result.json()

    def test_every_activity_page_is_regulated(self):
        self.send.side_effect = [response(200, [{}]*20), response(200, [{}]), response(200, [])]
        result = ge.safe_call(self.api.get_activities_by_date, "2026-01-01", "2026-01-31")
        self.assertEqual(21, len(result))
        self.assertEqual(3, self.limiter.wait.call_count)
        self.assertEqual(3, self.limiter.on_success.call_count)

    def test_real_client_reconfiguration_keeps_hidden_retries_disabled(self):
        api = ge.Garmin()
        regulate_transport(api, lambda: self.limiter)
        try:
            api.garth.configure(domain="garmin.com")
            self.assertEqual(0, api.garth.sess.get_adapter("https://example.invalid").max_retries.total)
            self.assertTrue(api._export_transport_regulated)
        finally:
            api.garth.sess.close()

    def test_real_oauth_session_shares_the_regulated_adapter(self):
        from garth import sso
        api = ge.Garmin()
        regulate_transport(api, lambda: self.limiter)
        api.garth.configure(domain="garmin.com")
        self.send.return_value = response(200, {})
        try:
            with patch.dict(sso.OAUTH_CONSUMER, {"consumer_key": "synthetic", "consumer_secret": "synthetic"}):
                oauth = sso.GarminOAuth1Session(parent=api.garth.sess)
                oauth.get("https://example.invalid/oauth")
                oauth.close()
            self.assertEqual(1, self.limiter.wait.call_count)
            self.assertEqual(1, self.limiter.on_success.call_count)
        finally:
            api.garth.sess.close()

    def test_environment_cannot_enable_telemetry(self):
        environment = dict(os.environ, GARTH_TELEMETRY="true")
        result = subprocess.run([sys.executable, "-c",
            "import garmin_export as ge; api=ge.Garmin(); assert not api.garth.telemetry.enabled; api.garth.configure(); assert not api.garth.telemetry.enabled"],
            env=environment, capture_output=True, timeout=30)
        self.assertEqual(0, result.returncode, result.stderr.decode(errors="replace"))

    def test_second_429_renews_barrier_without_third_attempt(self):
        self.send.side_effect = [response(429, {}), response(429, {})]
        value, succeeded = ge._safe_call_with_status(self.api.connectapi, "/activities")
        self.assertIsNone(value)
        self.assertFalse(succeeded)
        self.assertEqual(2, self.send.call_count)
        self.assertEqual(2, self.limiter.on_rate_limit.call_count)

    def test_library_errors_do_not_expose_raw_messages_even_in_verbose(self):
        capture = io.StringIO()
        handler = logging.StreamHandler(capture)
        root = logging.getLogger()
        root.addHandler(handler)
        try:
            configure_private_logging(verbose=True)
            self.send.side_effect = requests.ConnectionError("PRIVATE_SYNTHETIC_TOKEN https://example.invalid/private")
            value, succeeded = ge._safe_call_with_status(self.api.connectapi, "/activity/123456789")
            self.assertFalse(succeeded)
            text = capture.getvalue()
            self.assertNotIn("PRIVATE_SYNTHETIC_TOKEN", text)
            self.assertNotIn("123456789", text)
            self.assertNotIn("Traceback", text)
            self.assertIn("Fallo de connectapi", text)
        finally:
            root.removeHandler(handler)
            configure_private_logging()

    def test_http_400_is_a_failure_not_a_successful_empty_result(self):
        wrapped = requests.HTTPError(response=response(400, {}))
        with patch.object(ge, "_limiter", Mock()):
            _, succeeded = ge._safe_call_with_status(Mock(side_effect=ge.GarthHTTPError("synthetic", wrapped)))
        self.assertFalse(succeeded)


class ProfileBindingTests(unittest.TestCase):
    def test_different_account_is_rejected_without_changing_binding(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            verify_account(Mock(get_user_profile=Mock(return_value={"id": 123456789})), root)
            path = root / ".account_binding.json"
            original = path.read_bytes()
            with self.assertRaises(ProfileBindingError):
                verify_account(Mock(get_user_profile=Mock(return_value={"id": 987654321})), root)
            self.assertEqual(original, path.read_bytes())
            self.assertNotIn(b"123456789", original)

    def test_migration_checks_owner_in_legacy_cache(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            cache = ge.ExportCache(root, cache_dir=root)
            cache.put_section("profile", {"user_profile": {"id": 123456789}})
            with self.assertRaises(ProfileBindingError):
                verify_account(Mock(get_user_profile=Mock(return_value={"id": 987654321})), root, trusted_existing_session=True)
            self.assertFalse((root / ".account_binding.json").exists())
            verify_account(Mock(get_user_profile=Mock(return_value={"id": 123456789})), root)

    def test_failed_force_login_preserves_old_tokens(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            old = Mock(get_user_profile=Mock(return_value={"id": 123456789}))
            verify_account(old, root)
            api = Mock(get_user_profile=Mock(return_value={"id": 987654321}))
            api.login.return_value = (None, None)
            with patch.object(ge, "Garmin", return_value=api), patch.object(ge, "regulate_transport", side_effect=lambda client, _: client), \
                 patch.object(ge, "_persist_auth_tokens") as persist, patch("builtins.input", return_value="synthetic"), \
                 patch.object(ge, "getpass", return_value="synthetic"):
                with self.assertRaises(ProfileBindingError):
                    ge.authenticate(str(root/"tokens"), force_login=True, use_credential_environment=False, cache_dir=root)
                persist.assert_not_called()


class ReportRegressionTests(unittest.TestCase):
    def test_catalog_distinguishes_valid_empty_from_invalid_entries(self):
        start, end = date(2026,1,1), date(2026,1,31)
        valid = {"activityId": 123, "startTimeLocal": "2026-01-10 10:00:00"}
        self.assertEqual([], ta.activity_catalog_document([], b"synthetic", start, end)["activities"])
        for invalid in ([{}], [valid, valid], [{**valid, "startTimeLocal": "2025-12-31"}], None):
            with self.subTest(invalid=invalid), self.assertRaises(ValueError):
                ta.activity_catalog_document(invalid, b"synthetic", start, end)

    def test_daily_journal_accepts_launcher_empty_activity(self):
        entries = ta.normalise_journal({"entries": [{"date": "2026-01-11T00:00:00", "activityId": "",
            "privateComment": "Descanso ficticio", "includeCommentInExport": True}]})
        self.assertNotIn("activity_ref", entries[0])
        self.assertEqual("Descanso ficticio", entries[0]["note"])
        self.assertEqual("daily", entries[0]["entry_type"])

    def test_period_summary_and_timeline_use_same_population(self):
        activities = [{"activity_ref": f"activity_{i:012x}", "date": day, "sport": "running",
                       "distance_m": 5000, "duration_s": 1800}
                      for i, day in enumerate(["2026-01-01", "2026-01-11"])]
        result = ta.build_report_extensions(activities, [], date(2026,1,11), date(2026,1,11))
        self.assertEqual(1, len(result["activities"]))
        self.assertEqual(0, result["period_summary"]["training"]["days_without_recorded_training"])

    def test_sparse_series_does_not_claim_complete_coverage(self):
        result = ta.calculate_cardiac_drift(self.activity(list(range(20))))
        self.assertEqual("not_eligible", result["status"])
        self.assertLess(result["series_coverage_pct"], 1)

    @staticmethod
    def activity(times):
        return {"sport": "running", "duration_s": 3600, "activity_series": {
            "metric_descriptors": [{"field": "duration_raw", "source_unit": "second"},
                                   {"field": "speed_raw"}, {"field": "heart_rate_raw"}],
            "samples": [[t, 3, 140 if t <= 1800 else 150] for t in times]}}

    def test_covered_series_uses_temporal_weights(self):
        result = ta.calculate_cardiac_drift(self.activity(list(range(0,3601,10))))
        self.assertEqual("available", result["status"])
        self.assertEqual(100, result["series_coverage_pct"])
        self.assertAlmostEqual(7.1, result["cardiac_drift_pct"], places=1)

    def test_unknown_time_unit_and_disordered_rows_are_rejected(self):
        activity = self.activity(list(range(0,3601,10)))
        activity["activity_series"]["metric_descriptors"][0]["source_unit"] = "unknown"
        self.assertEqual("duration_unit_unconfirmed", ta.calculate_cardiac_drift(activity)["reason"])
        activity = self.activity([0, 20, 10, 30])
        self.assertEqual("non_increasing_timestamps", ta.calculate_cardiac_drift(activity)["reason"])

    def test_failed_atomic_cache_write_preserves_previous_entry(self):
        with tempfile.TemporaryDirectory() as directory:
            cache = ge.ExportCache(Path(directory))
            cache.put_day("2026-01-11", {"sleep": 123})
            with patch("export_io.os.replace", side_effect=OSError("synthetic")):
                with self.assertRaises(OSError):
                    cache.put_day("2026-01-11", {"sleep": 456})
            self.assertEqual({"sleep": 123}, cache.get_day("2026-01-11"))

    def test_section_refresh_failure_preserves_data_and_retries(self):
        with tempfile.TemporaryDirectory() as directory:
            cache = ge.ExportCache(Path(directory))
            cache.put_refreshable_section("goals", {"record": 42}, complete=True)
            self.assertFalse(cache.section_needs_refresh("goals", 1))
            result = cache.put_refreshable_section("goals", {"record": None}, complete=False)
            self.assertEqual({"record": 42}, result)
            self.assertEqual(result, cache.get_section("goals"))
            self.assertTrue(cache.section_needs_refresh("goals", 1))
            cache.put_refreshable_section("goals", {"record": None}, complete=True)
            self.assertEqual({"record": None}, cache.get_section("goals"))
            self.assertFalse(cache.section_needs_refresh("goals", 1))

    def test_xlsx_real_metadata_section_does_not_serialize_text(self):
        with tempfile.TemporaryDirectory() as directory:
            exporter = ge.GarminExporter(Mock(), Path(directory), 1, 10, output_format="xlsx")
            with patch.object(ge, "_compact_mode", True), patch.object(ge, "_json", side_effect=AssertionError("TXT serialization")):
                exporter.export_metadata()
            self.assertEqual([], exporter.md)
            self.assertIn("export_metadata", exporter.semantic_model)

    def test_xlsx_real_activity_with_series_does_not_serialize_text(self):
        api = Mock()
        activity = {"activityId": 42, "activityName": "Rodaje ficticio",
            "activityType": {"typeKey": "running"}, "startTimeLocal": "2026-01-12 08:00:00"}
        series = {"metricDescriptors": [{"key": "directHeartRate", "metricsIndex": 0,
            "unit": {"key": "bpm", "factor": 1.0}}],
            "activityDetailMetrics": [{"metrics": [150], "offset": i} for i in range(10000)]}
        api.get_activities_by_date.return_value = [activity]
        api.get_activity_details.return_value = series
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            cache = ge.ExportCache(root)
            cache.put_activity(42, {"summary": activity, "detail": {"distance": 21097}, "details": series})
            exporter = ge.GarminExporter(api, root, 8, 100, cache=cache,
                explicit_start_date=date(2026,1,10), explicit_end_date=date(2026,1,17),
                include_activity_details=True, output_format="xlsx")
            with patch.object(ge, "_compact_mode", True), patch.object(ge, "_limiter", Mock()), \
                 patch.object(ge, "_json", side_effect=AssertionError("TXT serialization")):
                exporter.export_activities()
            self.assertEqual([], exporter.md)
            self.assertEqual(10000, len(exporter.semantic_model["activities"][0]["activity_series"]["samples"]))

    def test_events_only_include_controlled_data(self):
        output = io.StringIO()
        EventEmitter(True, output).emit("progress", phase="Activities", completed=1, total=3)
        event = json.loads(output.getvalue().split(" ",1)[1])
        self.assertEqual(1, event["protocol_version"])
        self.assertEqual(1, event["completed"])
        with self.assertRaises(ValueError):
            EventEmitter(True, output).emit("phase", phase="https://example.invalid")


if __name__ == "__main__":
    unittest.main()
