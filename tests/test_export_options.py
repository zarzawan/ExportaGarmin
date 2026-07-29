import json
import tempfile
import unittest
from contextlib import ExitStack
from datetime import date, timedelta
from pathlib import Path
from unittest.mock import Mock, patch

import garmin_export
from garth.exc import GarthHTTPError
from openpyxl import Workbook
from garmin_export import (
    ExportCache,
    GarminExporter,
    RateLimiter,
    _chunked_date_call,
    _normalise_output_filename,
    safe_call,
)


class OutputFilenameTests(unittest.TestCase):
    def test_adds_txt_extension(self):
        self.assertEqual(
            "garmin_datos_2026-01-01_a_2026-01-31.txt",
            _normalise_output_filename(
                "garmin_datos_2026-01-01_a_2026-01-31"
            ),
        )

    def test_rejects_paths(self):
        with self.assertRaisesRegex(ValueError, "sin carpetas"):
            _normalise_output_filename(r"otra\carpeta\datos.txt")


class RateLimiterTests(unittest.TestCase):
    def test_rate_limit_creates_one_shared_barrier(self):
        limiter = RateLimiter(base_delay=0)
        clock = [0.0]
        waits = []

        def fake_sleep(seconds):
            waits.append(seconds)
            clock[0] += seconds

        with (
            patch("garmin_export.time.monotonic", side_effect=lambda: clock[0]),
            patch("garmin_export.time.sleep", side_effect=fake_sleep),
        ):
            limiter.on_rate_limit()
            limiter.wait()

        self.assertEqual(60.0, waits[0])
        self.assertGreaterEqual(limiter.last_call, limiter.blocked_until)

    def test_garth_429_waits_and_retries_once(self):
        wrapped_error = Mock()
        wrapped_error.response = Mock(status_code=429)
        rate_error = GarthHTTPError("mensaje privado", wrapped_error)
        endpoint = Mock(side_effect=[rate_error, {"ok": True}])
        endpoint.__name__ = "endpoint_de_prueba"
        limiter = Mock()

        with patch.object(garmin_export, "_limiter", limiter):
            result = safe_call(endpoint)

        self.assertEqual({"ok": True}, result)
        self.assertEqual(2, endpoint.call_count)
        self.assertEqual(2, limiter.wait.call_count)
        limiter.on_rate_limit.assert_called_once_with()
        limiter.on_success.assert_called_once_with()


class DateChunkTests(unittest.TestCase):
    def setUp(self):
        self.previous_limiter = garmin_export._limiter
        garmin_export._limiter = RateLimiter(base_delay=0)

    def tearDown(self):
        garmin_export._limiter = self.previous_limiter

    def test_single_day_range_still_calls_api(self):
        api_call = Mock(return_value=[{"date": "2026-01-10"}])

        result = _chunked_date_call(
            api_call,
            date(2026, 1, 10),
            date(2026, 1, 10),
            "single_day",
        )

        api_call.assert_called_once_with("2026-01-10", "2026-01-10")
        self.assertEqual([{"date": "2026-01-10"}], result)


class BloodPressureTests(unittest.TestCase):
    def setUp(self):
        self.previous_compact_mode = garmin_export._compact_mode
        self.previous_limiter = garmin_export._limiter
        garmin_export._compact_mode = True
        garmin_export._limiter = RateLimiter(base_delay=0)

    def tearDown(self):
        garmin_export._compact_mode = self.previous_compact_mode
        garmin_export._limiter = self.previous_limiter

    def test_exports_selected_blood_pressure_range(self):
        api = Mock()
        api.get_blood_pressure.return_value = {
            "measurementSummaries": [
                {"systolic": 120, "diastolic": 75}
            ]
        }
        exporter = GarminExporter(
            api=api,
            out_dir=Path("export"),
            days=30,
            max_activities=100,
            explicit_start_date=date(2026, 1, 10),
            explicit_end_date=date(2026, 1, 17),
        )

        exporter.export_blood_pressure()

        api.get_blood_pressure.assert_called_once_with(
            "2026-01-10", "2026-01-17"
        )
        self.assertIn("Blood Pressure", "\n".join(exporter.md))
        self.assertIn('"systolic_mmhg": 120', "\n".join(exporter.md))


class XlsxOnlyRenderingTests(unittest.TestCase):
    def test_xlsx_only_does_not_build_the_text_and_logs_its_duration(self):
        section_methods = (
            "export_metadata",
            "export_profile",
            "export_daily_health",
            "export_blood_pressure",
            "export_activities",
            "export_body_composition",
            "export_training",
            "export_goals",
            "export_gear",
            "export_hydration",
            "export_nutrition",
        )

        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            exporter = GarminExporter(
                Mock(),
                root,
                days=1,
                max_activities=1,
                cache=ExportCache(root, enabled=False),
                explicit_start_date=date(2026, 1, 1),
                explicit_end_date=date(2026, 1, 1),
                output_format="xlsx",
            )
            with ExitStack() as stack:
                stack.enter_context(
                    patch.object(garmin_export, "_compact_mode", True)
                )
                stack.enter_context(
                    patch.object(garmin_export, "_split_mode", False)
                )
                for method_name in section_methods:
                    stack.enter_context(
                        patch.object(exporter, method_name, return_value=None)
                    )
                stack.enter_context(
                    patch.object(
                        exporter,
                        "_finalize_semantic_model",
                        return_value=None,
                    )
                )
                text_renderer = stack.enter_context(
                    patch.object(exporter, "_render_compact_text")
                )

                def fake_render_xlsx(_model, path):
                    path.write_bytes(b"xlsx")

                xlsx_renderer = stack.enter_context(
                    patch(
                        "garmin_export.render_xlsx",
                        side_effect=fake_render_xlsx,
                    )
                )
                with self.assertLogs("garmin_export", level="INFO") as captured:
                    exporter.run()

        text_renderer.assert_not_called()
        xlsx_renderer.assert_called_once()
        messages = "\n".join(captured.output)
        self.assertIn("Creando Excel opcional", messages)
        self.assertIn("Excel creado en", messages)


class ActivityDetailsTests(unittest.TestCase):
    def setUp(self):
        self.previous_compact_mode = garmin_export._compact_mode
        self.previous_limiter = garmin_export._limiter
        garmin_export._compact_mode = True
        garmin_export._limiter = RateLimiter(base_delay=0)

    def tearDown(self):
        garmin_export._compact_mode = self.previous_compact_mode
        garmin_export._limiter = self.previous_limiter

    def _export_activity(self, include_details):
        api = Mock()
        activity = {
            "activityId": 42,
            "activityName": "Tirada larga",
            "activityType": {"typeKey": "running"},
            "startTimeLocal": "2026-01-12 08:00:00",
        }
        api.get_activities_by_date.return_value = [activity]
        series_payload = {
            "metricDescriptors": [
                {
                    "key": "directHeartRate",
                    "metricsIndex": 0,
                    "unit": {"key": "bpm", "factor": 1.0},
                },
                {
                    "key": "directLatitude",
                    "metricsIndex": 1,
                    "unit": {"key": "degree", "factor": 1.0},
                }
            ],
            "activityDetailMetrics": [
                {"metrics": [150, 39.0], "offset": 0},
                {"metrics": [151, 39.1], "offset": 1},
            ],
        }
        api.get_activity_details.return_value = series_payload

        with tempfile.TemporaryDirectory() as temp_dir:
            cache = ExportCache(Path(temp_dir), enabled=True)
            cache.put_activity(
                42,
                {
                    "summary": activity,
                    "detail": {"distance": 21097},
                    "details": series_payload,
                },
            )
            exporter = GarminExporter(
                api=api,
                out_dir=Path(temp_dir),
                days=30,
                max_activities=100,
                explicit_start_date=date(2026, 1, 10),
                explicit_end_date=date(2026, 1, 17),
                cache=cache,
                include_activity_details=include_details,
            )
            exporter.export_activities()
            output = "\n".join(exporter.md)

        data_line = next(
            line for line in output.splitlines()
            if line.startswith("[{")
        )
        return json.loads(data_line)[0]

    def test_compact_mode_omits_activity_details_by_default(self):
        self.assertNotIn("activity_series", self._export_activity(False))

    def test_compact_mode_can_include_activity_details(self):
        activity = self._export_activity(True)
        self.assertIn("activity_series", activity)
        series = activity["activity_series"]
        self.assertEqual(1, len(series["metric_descriptors"]))
        self.assertEqual(1, len(series["samples"][0]))
        self.assertNotIn("Latitude", json.dumps(series))

    def test_missing_cached_series_is_downloaded_when_details_are_requested(self):
        api = Mock()
        raw_activity = {
            "activityId": 42,
            "activityType": {"typeKey": "running"},
            "startTimeLocal": "2026-01-12 08:00:00",
        }
        api.get_activities_by_date.return_value = [raw_activity]
        api.get_activity_details.return_value = {
            "metricDescriptors": [{
                "key": "directHeartRate",
                "metricsIndex": 0,
                "unit": {"key": "bpm"},
            }],
            "activityDetailMetrics": [{"metrics": [150]}],
        }
        with tempfile.TemporaryDirectory() as temp_dir:
            root = Path(temp_dir)
            cache = ExportCache(root, enabled=True)
            cache.put_activity(42, {
                "summary": raw_activity,
                "detail": {"distance": 10_000},
                "details": None,
            })
            exporter = GarminExporter(
                api=api,
                out_dir=root,
                days=30,
                max_activities=100,
                explicit_start_date=date(2026, 1, 10),
                explicit_end_date=date(2026, 1, 17),
                cache=cache,
                include_activity_details=True,
            )

            exporter.export_activities()

        api.get_activity_details.assert_called_once_with(
            42,
            maxchart=100_000,
        )
        self.assertIn(
            "activity_series",
            exporter.compact_activities[0],
        )


    def test_limited_cached_series_is_refetched_for_full_details(self):
        api = Mock()
        raw_activity = {
            "activityId": 42,
            "activityType": {"typeKey": "running"},
            "startTimeLocal": "2026-01-12 08:00:00",
        }
        api.get_activities_by_date.return_value = [raw_activity]
        api.get_activity_details.return_value = {
            "metricDescriptors": [{
                "key": "directHeartRate",
                "metricsIndex": 0,
                "unit": {"key": "bpm"},
            }],
            "activityDetailMetrics": [{"metrics": [170]}],
        }

        with tempfile.TemporaryDirectory() as temp_dir:
            root = Path(temp_dir)
            cache = ExportCache(root, enabled=True)
            cache.put_activity(42, {
                "summary": raw_activity,
                "detail": {},
                "details": {
                    "metricDescriptors": [{
                        "key": "directHeartRate",
                        "metricsIndex": 0,
                    }],
                    "activityDetailMetrics": [{"metrics": [140]}],
                },
                "_details_maxchart_requested": 2_000,
                "gear": [],
            })
            exporter = GarminExporter(
                api=api,
                out_dir=root,
                days=60,
                max_activities=100,
                explicit_start_date=date(2026, 1, 1),
                explicit_end_date=date(2026, 2, 28),
                cache=cache,
                include_activity_details=True,
            )

            exporter.export_activities()

        api.get_activity_details.assert_called_once_with(
            42,
            maxchart=garmin_export._MAX_ACTIVITY_CHART_POINTS,
        )
        self.assertEqual(
            170,
            exporter.compact_activities[0]["activity_series"]["samples"][0][0],
        )


class UpdateDiscoveryTests(unittest.TestCase):
    @staticmethod
    def _exporter(root, manifest_path=None):
        return GarminExporter(
            api=Mock(),
            out_dir=root,
            days=30,
            max_activities=10,
            cache=ExportCache(
                root,
                enabled=False,
                cache_dir=root / ".cache",
            ),
            update_mode=True,
            manifest_path=manifest_path,
        )

    def test_update_reads_custom_txt_name(self):
        end_date = date.today() - timedelta(days=5)
        start_date = end_date - timedelta(days=30)
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / "mis_datos.txt").write_text(
                "Intervalo de fechas: "
                f"{start_date.isoformat()} a {end_date.isoformat()}\n",
                encoding="utf-8",
            )

            exporter = self._exporter(root)

        self.assertEqual(end_date - timedelta(days=1), exporter.start_date)

    def test_update_reads_manifest_for_custom_xlsx_output(self):
        end_date = date.today() - timedelta(days=3)
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            manifest = root / "ultimo-manifiesto.json"
            manifest.write_text(
                json.dumps({
                    "status": "completed",
                    "report_type": "history",
                    "end_date": end_date.isoformat(),
                    "files": ["archivo-personalizado.xlsx"],
                }),
                encoding="utf-8",
            )

            exporter = self._exporter(root, manifest)

        self.assertEqual(end_date - timedelta(days=1), exporter.start_date)

    def test_activity_manifest_is_not_used_as_incremental_base(self):
        activity_end = date.today() - timedelta(days=1)
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            manifest = root / "ultimo-manifiesto.json"
            manifest.write_text(
                json.dumps({
                    "status": "completed",
                    "report_type": "activity",
                    "end_date": activity_end.isoformat(),
                    "files": ["actividad.xlsx"],
                }),
                encoding="utf-8",
            )

            exporter = self._exporter(root, manifest)

        self.assertIsNone(exporter.update_base_date)

    def test_custom_activity_xlsx_is_not_used_as_incremental_base(self):
        activity_end = date.today() - timedelta(days=1)
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            workbook = Workbook()
            sheet = workbook.active
            sheet.title = "RESUMEN"
            sheet.append(["campo", "valor"])
            sheet.append(["period.end_date", activity_end.isoformat()])
            sheet.append(["export.report_type", "activity"])
            workbook.save(root / "nombre_personalizado.xlsx")
            workbook.close()

            exporter = self._exporter(root)

        self.assertIsNone(exporter.update_base_date)

    def test_partial_manifest_is_not_used_as_incremental_base(self):
        partial_end = date.today() - timedelta(days=1)
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            manifest = root / "ultimo-manifiesto.json"
            manifest.write_text(
                json.dumps({
                    "status": "partial",
                    "report_type": "history",
                    "end_date": partial_end.isoformat(),
                    "files": ["datos.xlsx"],
                }),
                encoding="utf-8",
            )

            exporter = self._exporter(root, manifest)

        self.assertIsNone(exporter.update_base_date)


if __name__ == "__main__":
    unittest.main()
