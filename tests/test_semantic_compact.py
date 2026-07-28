import json
import tempfile
import unittest
from datetime import date
from pathlib import Path
from unittest.mock import Mock

import garmin_export
from garmin_export import (
    ExportCache,
    GarminExporter,
    RateLimiter,
    _compact_activity,
    _compact_daily_record,
    _compact_hydration,
    _compact_lifestyle_entries,
    _compact_nutrition,
    _compact_profile,
    _compact_training,
    _find_blood_pressure_measurements,
    _weekly_summary,
)


class SemanticHelperTests(unittest.TestCase):
    def test_profile_excludes_identity_and_exact_birth_date(self):
        raw = {
            "full_name": "Persona de prueba",
            "user_profile": {
                "id": 999,
                "userData": {
                    "birthDate": "1980-05-20",
                    "gender": "MALE",
                    "height": 180,
                },
            },
            "profile_settings": {
                "measurementSystem": "metric",
                "timeZone": "Europe/Madrid",
            },
            "devices": [
                {
                    "deviceId": 44,
                    "productDisplayName": "Reloj de prueba",
                    "serialNumber": "SECRETO",
                }
            ],
            "primary_device": {
                "PrimaryTrainingDevice": {"deviceId": 44}
            },
        }

        result = _compact_profile(raw, date(2026, 7, 28))
        text = json.dumps(result)

        self.assertEqual("male", result["sex"])
        self.assertEqual("Reloj de prueba", result["primary_watch"])
        self.assertNotIn("Persona de prueba", text)
        self.assertNotIn("1980-05-20", text)
        self.assertNotIn("SECRETO", text)

    def test_daily_record_normalises_sleep_and_hrv(self):
        raw = {
            "summary": {
                "totalSteps": 9000,
                "restingHeartRate": 52,
                "userProfileId": 999,
            },
            "sleep": {
                "dailySleepDTO": {
                    "sleepTimeSeconds": 25200,
                    "deepSleepSeconds": 3600,
                    "sleepStartTimestampLocal": "2026-07-01T23:00:00",
                    "sleepEndTimestampLocal": "2026-07-02T06:30:00",
                    "sleepScores": {"overall": {"value": 82}},
                    "userProfilePK": 999,
                }
            },
            "hrv": {
                "hrvSummary": {
                    "calendarDate": "2026-07-02",
                    "lastNightAvg": 48,
                    "lastNight5MinHigh": 70,
                    "status": "BALANCED",
                },
                "hrvReadings": [{"hrvValue": 47}],
            },
        }

        result = _compact_daily_record("2026-07-02", raw)
        text = json.dumps(result)

        self.assertEqual(25200, result["sleep"]["total_sleep_s"])
        self.assertEqual(48, result["hrv"]["overnight_average_ms"])
        self.assertNotIn("hrvReadings", text)
        self.assertNotIn("userProfile", text)

    def test_activity_keeps_laps_zones_feedback_and_gear_without_location(self):
        raw = {
            "summary": {
                "activityId": 42,
                "activityName": "Rodaje",
                "activityType": {"typeKey": "running"},
                "startTimeLocal": "2026-07-20 08:00:00",
                "distance": 10000,
                "duration": 3600,
                "averageSpeed": 2.777777,
                "averageHR": 145,
                "ownerFullName": "No exportar",
                "startLatitude": 39.0,
            },
            "detail": {
                "summaryDTO": {
                    "directWorkoutFeel": 75,
                    "directWorkoutRpe": 50,
                }
            },
            "splits": {
                "lapDTOs": [
                    {
                        "lapIndex": 1,
                        "distance": 1000,
                        "duration": 350,
                        "averageSpeed": 2.857,
                    }
                ]
            },
            "hr_zones": [
                {"zoneNumber": 2, "secsInZone": 1800, "zoneLowBoundary": 130}
            ],
            "gear": [
                {
                    "uuid": "gear-1",
                    "displayName": "Zapatillas de prueba",
                    "gearTypeName": "running_shoes",
                }
            ],
        }

        result = _compact_activity(raw)
        text = json.dumps(result)

        self.assertEqual(1, len(result["laps"]))
        self.assertEqual(
            5,
            result["self_evaluation"]["perceived_exertion_1_10"],
        )
        self.assertEqual("Zapatillas de prueba", result["gear"][0]["name"])
        self.assertNotIn("latitude", text.lower())
        self.assertNotIn("No exportar", text)

    def test_training_separates_future_snapshot(self):
        raw = {
            "morning_readiness": {
                "calendarDate": "2026-07-12",
                "score": 70,
            },
            "race_predictions": {
                "calendarDate": "2026-07-28",
                "time5K": 1500,
            },
            "cycling_ftp": {
                "calendarDate": "2026-06-20",
                "functionalThresholdPower": 250,
            },
        }

        result, snapshots = _compact_training(
            raw,
            date(2026, 7, 6),
            date(2026, 7, 12),
        )

        self.assertIn(
            "morning_readiness",
            result["historical_period_data"],
        )
        self.assertIn("race_predictions", result["current_snapshot"])
        self.assertIn(
            "cycling_ftp",
            result["latest_before_or_within_period"],
        )
        self.assertEqual("race_predictions", snapshots[0]["metric"])

    def test_empty_blood_pressure_is_not_a_measurement(self):
        self.assertEqual(
            [],
            _find_blood_pressure_measurements({
                "from": "2026-07-01",
                "until": "2026-07-07",
                "measurementSummaries": [],
            }),
        )

    def test_hydration_goal_only_is_omitted(self):
        self.assertIsNone(_compact_hydration({"goalInML": 2000}))
        self.assertEqual(
            750,
            _compact_hydration({"goalInML": 2000, "valueInML": 750})[
                "intake_ml"
            ],
        )

    def test_nutrition_settings_without_food_are_omitted(self):
        self.assertIsNone(_compact_nutrition({
            "food_log": {"loggedFoodsWithServingSizes": []},
            "settings": {"calorieGoal": 2000},
        }))

    def test_lifestyle_keeps_real_logs_but_excludes_intimate_behaviours(self):
        result = _compact_lifestyle_entries({
            "dailyLogsReport": [
                {"name": "Illness", "logStatus": "YES"},
                {"name": "Sexo en pareja", "logStatus": "YES"},
                {"name": "Unlogged catalogue entry"},
            ]
        })

        self.assertEqual(["Illness"], [item["behaviour"] for item in result])

    def test_weekly_summary_uses_activities_once(self):
        result = _weekly_summary(
            [
                {
                    "sport": "running",
                    "distance_m": 10000,
                    "duration_s": 3600,
                    "training_load": 90,
                    "hr_zones": [{"zone": 2, "duration_s": 1800}],
                }
            ],
            [
                {
                    "steps": 12000,
                    "resting_heart_rate_bpm": 52,
                    "sleep": {"total_sleep_s": 25200},
                    "hrv": {"overnight_average_ms": 48},
                }
            ],
        )

        summary = result["weekly_summary"]
        self.assertEqual(1, summary["running_sessions"])
        self.assertEqual(10000, summary["running_distance_m"])
        self.assertEqual(3600, summary["total_training_duration_s"])


class SemanticExporterIntegrationTests(unittest.TestCase):
    def setUp(self):
        self.previous_compact_mode = garmin_export._compact_mode
        self.previous_limiter = garmin_export._limiter
        garmin_export._compact_mode = True
        garmin_export._limiter = RateLimiter(base_delay=0)

    def tearDown(self):
        garmin_export._compact_mode = self.previous_compact_mode
        garmin_export._limiter = self.previous_limiter

    def test_recent_cached_activity_refreshes_feedback_and_fetches_gear(self):
        today = date.today()
        activity = {
            "activityId": 42,
            "activityName": "Rodaje",
            "activityType": {"typeKey": "running"},
            "startTimeLocal": f"{today.isoformat()} 08:00:00",
        }
        api = Mock()
        api.get_activities_by_date.return_value = [activity]
        api.get_activity.return_value = {
            "summaryDTO": {
                "startTimeLocal": f"{today.isoformat()} 08:00:00",
                "directWorkoutRpe": 60,
            }
        }
        api.get_activity_gear.return_value = [
            {"uuid": "gear-1", "displayName": "Zapatillas"}
        ]

        with tempfile.TemporaryDirectory() as temp_dir:
            cache = ExportCache(Path(temp_dir), enabled=True)
            cache.put_activity(42, {"summary": activity, "detail": {}})
            exporter = GarminExporter(
                api=api,
                out_dir=Path(temp_dir),
                days=1,
                max_activities=10,
                explicit_start_date=today,
                explicit_end_date=today,
                cache=cache,
            )
            exporter.export_activities()

        api.get_activity.assert_called_once_with(42)
        api.get_activity_gear.assert_called_once_with(42)
        self.assertEqual(
            6,
            exporter.compact_activities[0]["self_evaluation"][
                "perceived_exertion_1_10"
            ],
        )
        self.assertEqual(
            "Zapatillas",
            exporter.compact_activities[0]["gear"][0]["name"],
        )

    def test_global_gear_uses_profile_id_returned_by_current_library(self):
        api = Mock()
        api.get_user_profile.return_value = {"id": 123}
        api.get_gear.return_value = [
            {"uuid": "gear-1", "displayName": "Bicicleta"}
        ]
        api.get_gear_defaults.return_value = []
        api.get_gear_stats.return_value = {"totalDistance": 50000}
        exporter = GarminExporter(
            api=api,
            out_dir=Path("export"),
            days=1,
            max_activities=10,
            explicit_start_date=date(2026, 1, 1),
            explicit_end_date=date(2026, 1, 1),
        )

        exporter.export_gear()

        api.get_gear.assert_called_once_with("123")
        self.assertIn('"name": "Bicicleta"', "\n".join(exporter.md))

    def test_full_profile_still_contains_raw_fields(self):
        garmin_export._compact_mode = False
        with tempfile.TemporaryDirectory() as temp_dir:
            cache = ExportCache(Path(temp_dir), enabled=True)
            cache.put_section("profile", {
                "full_name": "Nombre completo de prueba",
                "devices": [{"serialNumber": "SERIE"}],
            })
            exporter = GarminExporter(
                api=Mock(),
                out_dir=Path(temp_dir),
                days=1,
                max_activities=10,
                cache=cache,
            )
            exporter.export_profile()
            output = "\n".join(exporter.md)

        self.assertIn("Nombre completo de prueba", output)
        self.assertIn("SERIE", output)

    def test_full_activity_still_contains_raw_detail_and_location(self):
        garmin_export._compact_mode = False
        activity = {
            "activityId": 42,
            "activityName": "Actividad completa",
            "activityType": {"typeKey": "running"},
            "startTimeLocal": "2026-01-01 08:00:00",
            "ownerFullName": "Nombre raw",
            "startLatitude": 39.0,
        }
        api = Mock()
        api.get_activities_by_date.return_value = [activity]
        with tempfile.TemporaryDirectory() as temp_dir:
            cache = ExportCache(Path(temp_dir), enabled=True)
            cache.put_activity(42, {
                "summary": activity,
                "detail": {"summaryDTO": {"distance": 5000}},
                "details": {"activityDetailMetrics": [{"metrics": [1]}]},
            })
            exporter = GarminExporter(
                api=api,
                out_dir=Path(temp_dir),
                days=1,
                max_activities=10,
                explicit_start_date=date(2026, 1, 1),
                explicit_end_date=date(2026, 1, 1),
                cache=cache,
            )
            exporter.export_activities()
            output = "\n".join(exporter.md)

        self.assertIn("Nombre raw", output)
        self.assertIn("startLatitude", output)
        self.assertIn("Detalle de las series temporales", output)


if __name__ == "__main__":
    unittest.main()
