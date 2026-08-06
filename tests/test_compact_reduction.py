import copy
import json
import re
import tempfile
import unittest
from datetime import date, timedelta
from pathlib import Path
from unittest.mock import Mock

import garmin_export
from garmin_export import (
    ExportCache,
    GarminExporter,
    _activity_source_data,
    _compact_activity,
)
from training_analysis import (
    SCHEMA_VERSION,
    build_quality_report,
    build_report_extensions,
)


class CompactReductionRegressionTests(unittest.TestCase):
    START = date(2026, 4, 10)
    END = date(2026, 7, 30)
    SECRET = b"synthetic-regression-secret-32b"

    @staticmethod
    def _raw_activity(activity_id, sport, day):
        laps = [
            {
                "lapIndex": index,
                "distance": 1000,
                "duration": 330 + index,
                "averageSpeed": 3.0,
                "averageHR": 140 + index,
                "averagePower": 245 + index,
                "averageRunCadence": 172,
                "averageRespirationRate": 15.0 + index / 10,
                "elevationGain": 6.0,
                "elevationLoss": 4.0,
                "startElevation": 100.0 + index,
                "endElevation": 102.0 + index,
                "calories": 70,
            }
            for index in range(1, 5)
        ]
        summary = {
            "activityId": activity_id,
            "activityName": f"Sesión ficticia {activity_id}",
            "activityType": {"typeKey": sport},
            "startTimeLocal": f"{day.isoformat()} 08:00:00",
            "distance": 10_000 if sport == "running" else 20_000,
            "duration": 3600,
            "averageSpeed": 3.0,
            "averageHR": 145,
            "averagePower": 250,
            "averageRunCadence": 172,
            "elevationGain": 50,
            "elevationLoss": 45,
            "startElevation": 100,
            "endElevation": 105,
            "vo2MaxValue": 50.0,
            "splitSummaries": copy.deepcopy(laps),
        }
        return {
            "summary": summary,
            "detail": {
                "summaryDTO": copy.deepcopy(summary),
                "splitSummaries": copy.deepcopy(laps),
                "metadataDTO": {"hasPolyline": True, "activityId": activity_id},
                "hasPolyline": True,
                "encodedPolyline": "abc123",
            },
            "splits": {"lapDTOs": copy.deepcopy(laps)},
            "typed_splits": {"splits": copy.deepcopy(laps)},
            "split_summaries": {"splitSummaries": copy.deepcopy(laps)},
            "hr_zones": [
                {"zoneNumber": 2, "secsInZone": 1800, "zoneLowBoundary": 130}
            ],
            "power_zones": [
                {"zoneNumber": 3, "secsInZone": 900, "zoneLowBoundary": 230}
            ],
            "weather": {"temp": 68, "relativeHumidity": 55},
            "gear": [{
                "uuid": "synthetic-shoes",
                "displayName": "Zapatillas ficticias",
                "gearMakeName": "Marca ficticia",
                "gearModelName": "Modelo ficticio",
            }],
        }

    def _dataset(self):
        sports = (
            ["running"] * 60
            + ["cycling"] * 16
            + ["strength_training"]
            + ["walking"] * 14
        )
        raw_rows = []
        compact_rows = []
        for index, sport in enumerate(sports, 1):
            day = self.START + timedelta(days=index % 112)
            raw = self._raw_activity(index, sport, day)
            raw_rows.append(raw)
            compact_rows.append(_compact_activity(
                raw,
                reference_secret=self.SECRET,
            ))
        return raw_rows, compact_rows

    def _corrected_92_activity_dataset(self):
        sports = (
            ["running"] * 61
            + ["cycling"] * 16
            + ["strength_training"]
            + ["walking"] * 14
        )
        interval_types = (
            "RWD_RUN",
            "RWD_WALK",
            "RWD_STAND",
            "INTERVAL_ACTIVE",
            "INTERVAL_REST",
            "INTERVAL_WARMUP",
            "INTERVAL_COOLDOWN",
        )
        raw_rows = []
        primary_intervals = []
        for activity_index, sport in enumerate(sports):
            raw = self._raw_activity(
                activity_index + 1,
                sport,
                self.START + timedelta(days=activity_index % 112),
            )
            lap_count = 6 if activity_index < 65 else 5
            template = raw["splits"]["lapDTOs"][0]
            laps = []
            for lap_index in range(1, lap_count + 1):
                lap = copy.deepcopy(template)
                lap.update({
                    "lapIndex": lap_index,
                    "duration": 330 + lap_index,
                    "averageHR": 140 + lap_index,
                })
                laps.append(lap)
            raw["splits"] = {"lapDTOs": laps}
            raw["typed_splits"] = {"splits": copy.deepcopy(laps)}
            raw["summary"].pop("splitSummaries", None)
            raw["detail"]["summaryDTO"] = {}
            raw["detail"].pop("splitSummaries", None)

            count = 4 if activity_index < 22 else 3 if activity_index < 60 else 0
            intervals = []
            for local_index in range(count):
                sequence = sum(len(rows) for rows in primary_intervals) + local_index
                interval = {
                    "splitType": interval_types[sequence % len(interval_types)],
                    "noOfSplits": local_index + 1,
                    "totalDistance": round(
                        1000.0 + activity_index * 10 + local_index * 100,
                        1,
                    ),
                    "totalDuration": round(
                        320.0 + activity_index + local_index * 30,
                        1,
                    ),
                    "totalMovingDuration": round(
                        310.0 + activity_index + local_index * 29,
                        1,
                    ),
                    "elevationGain": float(local_index + 1),
                    "elevationLoss": float(local_index) / 2,
                    "averageHR": 140 + local_index,
                }
                intervals.append(interval)
            if activity_index == 0:
                intervals[0] = {
                    "splitType": "RWD_RUN",
                    "noOfSplits": 14,
                    "totalDistance": 6942.7,
                    "totalDuration": 2368.9,
                    "totalMovingDuration": 2346.0,
                    "elevationGain": 3.0,
                    "averageHR": 145,
                }
            primary_intervals.append(intervals)
            raw["split_summaries"] = {
                "splitSummaries": copy.deepcopy(intervals)
            }
            raw_rows.append(raw)

        secondary_intervals = [[] for _ in raw_rows]
        for activity_index in range(60):
            secondary_intervals[activity_index].append(
                copy.deepcopy(primary_intervals[activity_index][0])
            )
        duplicates_remaining = 184 - 60
        for activity_index in range(60):
            for interval in primary_intervals[activity_index][1:]:
                if duplicates_remaining <= 0:
                    break
                secondary_intervals[activity_index].append(copy.deepcopy(interval))
                duplicates_remaining -= 1
        self.assertEqual(0, duplicates_remaining)

        for activity_index, rows in enumerate(secondary_intervals):
            for row in rows:
                row["totalDistance"] = round(row["totalDistance"] + 0.1, 1)
                row["totalDuration"] = round(row["totalDuration"] + 0.05, 2)
                if "elevationGain" in row:
                    row["totalAscent"] = row.pop("elevationGain") * 100
                row["averageMovingSpeed"] = (
                    row.get("totalDistance", 0)
                    / row.get("totalMovingDuration", 1)
                )
            raw_rows[activity_index]["summary"]["splitSummaries"] = rows

        compact_rows = [
            _compact_activity(raw, reference_secret=self.SECRET)
            for raw in raw_rows
        ]
        return raw_rows, compact_rows

    def test_91_activities_are_unique_and_totals_are_preserved(self):
        _, activities = self._dataset()
        extensions = build_report_extensions(
            activities,
            [],
            self.START,
            self.END,
        )
        references = [item["activity_ref"] for item in activities]

        self.assertEqual(91, len(activities))
        self.assertEqual(91, len(set(references)))
        summary = extensions["period_summary"]
        self.assertEqual(91, summary["training"]["sessions_total"])
        self.assertEqual(60, summary["running"]["sessions"])
        self.assertEqual(16, summary["cycling"]["sessions"])
        self.assertEqual(1, summary["strength"]["sessions"])
        self.assertEqual(14, summary["other"]["sessions"])
        self.assertNotIn("daily_metric_coverage", summary)

    def test_canonical_fields_replace_raw_duplicate_containers(self):
        _, activities = self._dataset()
        activity = activities[0]
        encoded = json.dumps(activity, ensure_ascii=False)

        self.assertEqual(4, len(activity["laps"]))
        self.assertEqual(15.1, activity["laps"][0]["average_respiration_rate_brpm"])
        self.assertEqual(50.0, activity["vo2_max_ml_kg_min"])
        self.assertEqual([activity["gear_refs"][0]], activity["gear_refs"])
        self.assertNotIn("gear", activity)
        self.assertNotIn("grade_adjusted_pace_source", encoded)
        self.assertNotIn("splitSummaries", encoded)
        self.assertNotIn("typed_splits", encoded)
        self.assertNotIn("\"lapDTOs\":", encoded)
        self.assertNotIn("hasPolyline", encoded)
        self.assertNotIn("unmapped_sport_data", activity)

    def test_distinct_split_summary_is_normalised_once(self):
        raw = self._raw_activity(1, "running", self.START)
        distinct = {
            "splitType": "RUN",
            "noOfSplits": 4,
            "totalDistance": 4000,
            "totalDuration": 1330,
            "averageSpeed": 3.01,
            "averageHR": 143,
            "elevationGain": 24,
            "elevationLoss": 16,
        }
        raw["summary"]["splitSummaries"] = [copy.deepcopy(distinct)]
        raw["detail"]["splitSummaries"] = [copy.deepcopy(distinct)]
        raw["split_summaries"]["splitSummaries"] = [copy.deepcopy(distinct)]

        activity = _compact_activity(raw, reference_secret=self.SECRET)

        self.assertEqual(1, len(activity["interval_summaries"]))
        interval = activity["interval_summaries"][0]
        self.assertEqual(4, interval["interval_count"])
        self.assertEqual(4000, interval["distance_m"])
        self.assertEqual(24, interval["elevation_gain_m"])

    def test_3_3_1_case_keeps_92_activities_525_laps_and_202_intervals(self):
        raw_rows, activities = self._corrected_92_activity_dataset()
        extensions = build_report_extensions(
            activities, [], self.START, self.END
        )
        summary = extensions["period_summary"]

        self.assertEqual(386, sum(
            len((raw.get("split_summaries") or {}).get("splitSummaries", []))
            + len((raw.get("summary") or {}).get("splitSummaries", []))
            for raw in raw_rows
        ))
        self.assertEqual(92, len(activities))
        self.assertEqual(92, len({row["activity_ref"] for row in activities}))
        self.assertEqual(525, sum(len(row["laps"]) for row in activities))
        self.assertEqual(202, sum(
            len(row.get("interval_summaries", [])) for row in activities
        ))
        self.assertEqual(92, summary["training"]["sessions_total"])
        self.assertEqual(61, summary["running"]["sessions"])
        self.assertEqual(16, summary["cycling"]["sessions"])
        self.assertEqual(1, summary["strength"]["sessions"])
        self.assertEqual(14, summary["other"]["sessions"])
        self.assertTrue(all(
            "unmapped_sport_data" not in row for row in activities
        ))

    def test_3_3_1_merges_scaled_secondary_ascent_and_calculates_both_paces(self):
        _, activities = self._corrected_92_activity_dataset()
        matching = [
            interval
            for interval in activities[0]["interval_summaries"]
            if interval.get("interval_type") == "RWD_RUN"
            and interval.get("interval_count") == 14
            and interval.get("distance_m") == 6942.7
            and interval.get("duration_s") == 2368.9
        ]

        self.assertEqual(1, len(matching))
        interval = matching[0]
        self.assertEqual(3.0, interval["elevation_gain_m"])
        self.assertNotEqual(300.0, interval["elevation_gain_m"])
        self.assertAlmostEqual(
            2368.9 / (6942.7 / 1000),
            interval["average_pace_s_per_km"],
            places=1,
        )
        self.assertAlmostEqual(
            2346.0 / (6942.7 / 1000),
            interval["moving_pace_s_per_km"],
            places=1,
        )

    def test_primary_interval_conflict_prefers_plausible_ascent(self):
        raw = self._raw_activity(1, "running", self.START)
        canonical = {
            "splitType": "RWD_RUN",
            "noOfSplits": 14,
            "totalDistance": 6942.7,
            "totalDuration": 2368.9,
            "elevationGain": 3.0,
        }
        conflicting = {
            **canonical,
            "elevationGain": 300.0,
            "maxHR": 180,
        }
        raw["summary"]["elevationGain"] = 50.0
        raw["split_summaries"] = {"splitSummaries": [canonical]}
        raw["detail"]["splitSummaries"] = [conflicting]
        raw["summary"]["splitSummaries"] = []

        activity = _compact_activity(raw, reference_secret=self.SECRET)
        interval = activity["interval_summaries"][0]

        self.assertEqual(3.0, interval["elevation_gain_m"])
        self.assertEqual(180, interval["maximum_heart_rate_bpm"])

    def test_3_3_1_complete_text_is_valid_and_below_900_kb(self):
        _, activities = self._corrected_92_activity_dataset()
        extensions = build_report_extensions(
            activities, [], self.START, self.END
        )
        model = {
            "export_metadata": {
                "schema_version": SCHEMA_VERSION,
                "exported_at": "2026-07-30T12:00:00+02:00",
            },
            "period_summary": extensions["period_summary"],
            "weekly_timeline": extensions["weekly_timeline"],
            "activities": activities,
            "race_analysis": extensions["race_analysis"],
            "data_quality": build_quality_report(
                [], activities, self.START, self.END
            ),
        }
        with tempfile.TemporaryDirectory() as directory:
            exporter = GarminExporter(
                api=Mock(),
                out_dir=Path(directory),
                days=112,
                max_activities=100,
                explicit_start_date=self.START,
                explicit_end_date=self.END,
                cache=ExportCache(Path(directory) / "cache", enabled=False),
            )
            exporter.semantic_model = model
            previous_mode = garmin_export._compact_mode
            garmin_export._compact_mode = True
            try:
                rendered = exporter._render_compact_text()
            finally:
                garmin_export._compact_mode = previous_mode

        json_lines = [
            line for line in rendered.splitlines()
            if line.startswith("{") or line.startswith("[")
        ]
        for line in json_lines:
            json.loads(line)
        self.assertLess(len(rendered.encode("utf-8")), 900_000)

    def test_interval_semantic_identities_are_unique_with_tolerance(self):
        _, activities = self._corrected_92_activity_dataset()
        for activity in activities:
            identities = [
                (
                    row.get("interval_type"),
                    row.get("interval_count"),
                    round(row.get("distance_m", 0), 0),
                    round(row.get("duration_s", 0), 0),
                )
                for row in activity.get("interval_summaries", [])
            ]
            self.assertEqual(len(identities), len(set(identities)))

    def test_remaining_sport_aliases_are_normalised_without_residual(self):
        raw = self._raw_activity(1, "running", self.START)
        raw["summary"].update({
            "avgVerticalOscillation": 8.2,
            "avgGroundContactTime": 242,
            "avgStrideLength": 112.5,
            "avgVerticalRatio": 7.4,
            "maxDoubleCadence": 190,
            "minTemperature": 11.0,
            "avgElevation": 104.2,
            "maxVerticalSpeed": 0.8,
            "trainingEffectLabel": "AEROBIC_BASE",
            "aerobicTrainingEffectMessage": "Base aeróbica",
            "anaerobicTrainingEffectMessage": "Sin beneficio anaeróbico",
            "averageBikingCadenceInRevPerMinute": 88,
            "maxBikingCadenceInRevPerMinute": 104,
            "strokes": 2400,
            "powerTimeInZone_1": 10.001,
            "powerTimeInZone_2": 20.002,
            "isAutoCalcCalories": True,
            "autoCalcCalories": 700,
            "isElevationCorrected": True,
            "elevationCorrected": True,
            "bmrCalories": 80,
        })
        raw["detail"]["summaryDTO"] = {
            "directWorkoutRpe": 10,
            "directWorkoutFeel": 50,
        }
        raw["power_zones"] = [
            {"zoneNumber": 1, "secsInZone": 10.0, "zoneLowBoundary": 100},
            {"zoneNumber": 2, "secsInZone": 20.0, "zoneLowBoundary": 150},
        ]
        raw["weather"].update({
            "windSpeed": 3.2,
            "windDirection": 225,
            "windDirectionCompassPoint": "SW",
        })

        activity = _compact_activity(raw, reference_secret=self.SECRET)

        self.assertEqual(8.2, activity["average_vertical_oscillation_cm"])
        self.assertEqual(242, activity["average_ground_contact_time_ms"])
        self.assertEqual(112.5, activity["average_stride_length_cm"])
        self.assertEqual(7.4, activity["average_vertical_ratio_pct"])
        self.assertEqual(190, activity["maximum_cadence_spm"])
        self.assertEqual(11.0, activity["minimum_temperature_c"])
        self.assertEqual(104.2, activity["average_elevation_m"])
        self.assertEqual(0.8, activity["maximum_vertical_speed_m_s"])
        self.assertEqual("AEROBIC_BASE", activity["training_effect_label"])
        self.assertEqual(88, activity["average_cycling_cadence_rpm"])
        self.assertEqual(104, activity["maximum_cycling_cadence_rpm"])
        self.assertEqual(2400, activity["stroke_count"])
        self.assertEqual(3.2, activity["wind_speed_m_s"])
        self.assertEqual(225, activity["wind_direction_deg"])
        self.assertEqual(10.0, activity["power_zones"][0]["duration_s"])
        self.assertEqual(20.0, activity["power_zones"][1]["duration_s"])
        self.assertEqual(
            {"perceived_exertion_1_10": 1.0, "feeling": "normal"},
            activity["self_evaluation"],
        )
        self.assertNotIn("unmapped_sport_data", activity)

    def test_missing_values_remain_missing_and_zero_remains_zero(self):
        raw = self._raw_activity(1, "running", self.START)
        raw["summary"].pop("averagePower")
        raw["detail"]["summaryDTO"].pop("averagePower")
        raw["summary"]["averageHR"] = 0
        activity = _compact_activity(raw, reference_secret=self.SECRET)

        self.assertNotIn("average_power_w", activity)
        self.assertIn("average_heart_rate_bpm", activity)
        self.assertEqual(0, activity["average_heart_rate_bpm"])

    def test_report_calculations_do_not_depend_on_raw_duplicates(self):
        raw_rows, activities = self._dataset()
        legacy = copy.deepcopy(activities)
        for activity, raw in zip(legacy, raw_rows):
            activity["unmapped_sport_data"] = _activity_source_data(raw)

        current_report = build_report_extensions(
            activities, [], self.START, self.END
        )
        legacy_report = build_report_extensions(
            legacy, [], self.START, self.END
        )
        for key in ("period_summary", "weekly_timeline", "race_analysis"):
            self.assertEqual(current_report[key], legacy_report[key])

    def test_rendered_json_blocks_parse_and_size_drops_more_than_half(self):
        raw_rows, activities = self._dataset()
        extensions = build_report_extensions(
            activities, [], self.START, self.END
        )
        model = {
            "export_metadata": {
                "schema_version": SCHEMA_VERSION,
                "exported_at": "2026-07-30T12:00:00+02:00",
            },
            "period_summary": extensions["period_summary"],
            "weekly_timeline": extensions["weekly_timeline"],
            "activities": activities,
            "race_analysis": extensions["race_analysis"],
            "data_quality": build_quality_report(
                [], activities, self.START, self.END
            ),
        }
        with tempfile.TemporaryDirectory() as directory:
            exporter = GarminExporter(
                api=Mock(),
                out_dir=Path(directory),
                days=112,
                max_activities=100,
                explicit_start_date=self.START,
                explicit_end_date=self.END,
                cache=ExportCache(Path(directory) / "cache", enabled=False),
            )
            exporter.semantic_model = model
            previous_mode = garmin_export._compact_mode
            garmin_export._compact_mode = True
            try:
                rendered = exporter._render_compact_text()
            finally:
                garmin_export._compact_mode = previous_mode

        json_lines = [
            line for line in rendered.splitlines()
            if line.startswith("{") or line.startswith("[")
        ]
        self.assertGreaterEqual(len(json_lines), 6)
        for line in json_lines:
            json.loads(line)

        legacy_activities = copy.deepcopy(activities)
        for activity, raw in zip(legacy_activities, raw_rows):
            activity["unmapped_sport_data"] = _activity_source_data(raw)
        current_size = len(rendered.encode("utf-8"))
        legacy_size = len(json.dumps(
            {**model, "activities": legacy_activities},
            ensure_ascii=False,
            separators=(",", ":"),
        ).encode("utf-8"))
        self.assertLess(current_size, legacy_size * 0.5)
        self.assertLess(current_size, 1_000_000)


if __name__ == "__main__":
    unittest.main()
