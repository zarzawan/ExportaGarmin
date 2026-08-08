import json
import tempfile
import unittest
import zipfile
from datetime import datetime, timedelta
from pathlib import Path

from openpyxl import load_workbook

from training_analysis import (
    SCHEMA_VERSION,
    XLSX_PRESENTATION_VERSION,
    build_quality_report,
    build_report_extensions,
    render_xlsx,
)


class HumanXlsxReportTests(unittest.TestCase):
    """Contrato del Excel humano con cantidades ficticias equivalentes."""

    @classmethod
    def setUpClass(cls):
        from tests.test_compact_reduction import CompactReductionRegressionTests

        source_case = CompactReductionRegressionTests(methodName="runTest")
        _, activities = source_case._corrected_92_activity_dataset()
        cls.start = source_case.START
        cls.end = source_case.END

        target_running_distance = 327_579.6
        running = [item for item in activities if item.get("sport") == "running"]
        # Dos registros accidentales de pocos segundos en un mismo día. Se
        # conservan como actividades, pero no deben alterar los indicadores.
        micro_date = running[0]["date"]
        for index, activity in enumerate(running[:2]):
            activity["date"] = micro_date
            activity["distance_m"] = 0.0
            activity["duration_s"] = 5.0 - index
            activity["moving_duration_s"] = 5.0 - index
        running[2]["sport"] = "trail_running"
        valid_running = running[2:]
        per_run = round(target_running_distance / len(valid_running), 6)
        assigned = 0.0
        for index, activity in enumerate(valid_running):
            distance = (
                round(target_running_distance - assigned, 6)
                if index == len(valid_running) - 1
                else per_run
            )
            activity["distance_m"] = distance
            assigned += distance

        # 845 zonas: 17 actividades con diez y 75 con nueve.
        for activity_index, activity in enumerate(activities):
            activity["hr_zones"] = [
                {
                    "zone": zone,
                    "duration_s": 300 + zone,
                    "low_boundary_bpm": 90 + zone * 15,
                }
                for zone in range(1, 6)
            ]
            power_count = 5 if activity_index < 17 else 4
            activity["power_zones"] = [
                {
                    "zone": zone,
                    "duration_s": 180 + zone,
                    "low_boundary_w": 80 + zone * 40,
                }
                for zone in range(1, power_count + 1)
            ]
            if activity_index % 3 == 0:
                activity["self_evaluation"] = {
                    "perceived_exertion_1_10": 5.0,
                    "feeling": "good",
                }
            activity["training_load"] = 60.0 + activity_index
            activity["training_effect_label"] = "AEROBIC_BASE"
            activity["start_time_bucket"] = "morning"
        activities[0]["self_evaluation"]["feeling"] = "strong"
        activities[1]["training_effect_label"] = "Other"

        gear = []
        for index in range(29):
            reference = f"gear_{index + 1:012x}"
            gear.append({
                "gear_ref": reference,
                "gear_name": (
                    "Mis Zapatillas Ñandú"
                    if index == 0
                    else f"Material ficticio {index + 1}"
                ),
                "manufacturer": "Marca ficticia",
                "model": f"Modelo {index + 1}",
                "type": "Shoes" if index < 20 else "Bike",
                "status": "retired" if index == 28 else "active",
                "total_distance_m": 100_000 + index * 1_000,
            })
        for index, activity in enumerate(activities):
            activity["gear_refs"] = [gear[index % len(gear)]["gear_ref"]]

        days = []
        for offset in range(112):
            day = cls.start + timedelta(days=offset)
            days.append({
                "date": day.isoformat(),
                "steps": 0 if offset == 0 else 8_000 + offset,
                "distance_m": 6_000 + offset,
                "active_calories_kcal": 500 + offset,
                "total_calories_kcal": 2_200 + offset,
                "moderate_intensity_minutes": 20,
                "vigorous_intensity_minutes": 10,
                "resting_heart_rate_bpm": 48 + offset % 5,
                "average_stress": 30 + offset % 8,
                "maximum_stress": 70,
                "body_battery_high": 80,
                "body_battery_low": 20,
                "body_battery_charged": 55,
                "body_battery_drained": 45,
                "average_spo2_pct": 96.0,
                "lowest_spo2_pct": 92.0,
                "average_waking_respiration_brpm": 14.2,
                "average_sleep_respiration_brpm": 12.8,
                "sleep": {
                    "valid_sleep": True,
                    "sleep_start_local": f"{day.isoformat()}T23:00:00+02:00",
                    "sleep_end_local": f"{(day + timedelta(days=1)).isoformat()}T07:00:00+02:00",
                    "total_sleep_s": 28_000 + offset,
                    "awake_s": 800,
                    "light_sleep_s": 15_000,
                    "deep_sleep_s": 6_000,
                    "rem_sleep_s": 7_000,
                    "nap_time_s": 0,
                    "sleep_score": None if offset == 0 else 82,
                    "sleep_score_qualifier": "GOOD",
                    "average_sleep_heart_rate_bpm": 46,
                    "average_sleep_stress": 18,
                    "average_sleep_spo2_pct": 95.0,
                },
                "hrv": {
                    "overnight_average_ms": 52,
                    "highest_five_min_average_ms": 70,
                    "weekly_average_ms": 50,
                    "baseline_balanced_low_ms": 42,
                    "baseline_balanced_high_ms": 62,
                    "status": "BALANCED",
                },
                "lifestyle_logs": (
                    [{
                        "date": day.isoformat(),
                        "behaviour": "travel",
                        "status": True,
                    }]
                    if offset == 10 else []
                ),
            })

        race_context = {
            "event": {
                "label": "Maratón ficticio de Valencia",
                "distance_type": "marathon",
                "distance_m": 42_195.0,
                "race_date": "2026-12-06",
                "days_remaining": 129,
                "weeks_remaining": 18.4,
            },
            "goal": {
                "target_time_s": 16_200,
                "target_pace_s_per_km": 383.9,
            },
            "availability": {
                "available_days_per_week": 5,
                "long_run_day": "Domingo",
                "restrictions": "Sin restricciones ficticias.",
            },
        }
        journal = [
            {
                "date": (cls.start + timedelta(days=index)).isoformat(),
                "activity_ref": activities[index]["activity_ref"],
                "note": "Nota ficticia = no es fórmula" if index == 0 else f"Nota ficticia {index + 1}",
                "intended_session_type": "easy" if index % 2 == 0 else "interval",
                "source": "user_provided",
            }
            for index in range(6)
        ]
        journal[0]["intended_session_type"] = None
        journal.append({
            "date": journal[0]["date"],
            "activity_ref": journal[0]["activity_ref"],
            "note": None,
            "intended_session_type": "Prueba",
            "source": "user_provided",
        })
        journal.append({
            "date": journal[0]["date"],
            "activity_ref": journal[0]["activity_ref"],
            "note": None,
            "intended_session_type": None,
            "source": "user_provided",
        })
        extensions = build_report_extensions(
            activities,
            days,
            cls.start,
            cls.end,
            race_context,
            journal,
        )
        extensions["activities"][2]["classification"] = {
            "type": "uncategorized",
            "source": "insufficient_evidence",
            "confidence": 0.0,
            "evidence": [],
        }
        first_interval = extensions["activities"][0]["interval_summaries"][0]
        first_interval.update({
            "interval_type": "INTERVAL",
            "distance_m": 50.0,
            "moving_duration_s": 1.0,
            "average_pace_s_per_km": 17_023.0,
            "moving_pace_s_per_km": 46_663.0,
            "best_pace_s_per_km": 9_999.0,
            "grade_adjusted_pace_s_per_km": 8_888.0,
        })
        quality = build_quality_report(
            days,
            extensions["activities"],
            cls.start,
            cls.end,
            legacy_quality={
                "unit_conversions": ["Conversión ficticia comprobada."],
                "duplicate_sources_removed": ["Duplicado ficticio eliminado."],
            },
        )
        composition = [
            {
                "date": (cls.start + timedelta(days=index * 6)).isoformat(),
                "weight_kg": 72.0 - index * 0.05,
                "bmi": 22.3,
                "body_fat_pct": 14.2,
                "body_water_pct": 58.1,
                "muscle_mass_kg": 57.0,
                "bone_mass_kg": 3.2,
            }
            for index in range(18)
        ]
        pressure = [
            {
                "timestamp": f"{(cls.start + timedelta(days=index * 15)).isoformat()}T08:30:00+02:00",
                "systolic_mmhg": 118 + index,
                "diastolic_mmhg": 72 + index,
                "pulse_bpm": 52 + index,
            }
            for index in range(7)
        ]
        cls.model = {
            "export_metadata": {
                "schema_version": SCHEMA_VERSION,
                "export_status": "completed",
                "exported_at": "2026-08-07T09:15:00+02:00",
            },
            "race_context": race_context,
            "profile": {"primary_watch": "Garmin ficticio 955"},
            "daily_health": days,
            "gear": gear,
            "journal": journal,
            "blood_pressure": pressure,
            "body_composition": composition,
            "data_quality": quality,
            **extensions,
        }
        cls.temp_dir = tempfile.TemporaryDirectory()
        cls.output = Path(cls.temp_dir.name) / "revision_ficticia_maraton.xlsx"
        render_xlsx(cls.model, cls.output)

    @classmethod
    def tearDownClass(cls):
        cls.temp_dir.cleanup()

    def open_book(self, **kwargs):
        return load_workbook(self.output, **kwargs)

    @staticmethod
    def table_values(sheet):
        table = next(iter(sheet.tables.values()))
        start, end = table.ref.split(":")
        from openpyxl.utils.cell import range_boundaries
        min_col, min_row, max_col, max_row = range_boundaries(table.ref)
        headers = [
            sheet.cell(min_row, column).value
            for column in range(min_col, max_col + 1)
        ]
        rows = []
        for row_index in range(min_row + 1, max_row + 1):
            rows.append({
                header: sheet.cell(row_index, column_index).value
                for header, column_index in zip(
                    headers, range(min_col, max_col + 1)
                )
            })
        return rows

    def test_exact_equivalent_counts_and_totals_are_preserved(self):
        workbook = self.open_book(data_only=False)
        try:
            self.assertEqual(92, len(self.table_values(workbook["ACTIVIDADES"])))
            self.assertEqual(202, len(self.table_values(workbook["INTERVALOS"])))
            self.assertEqual(525, len(self.table_values(workbook["VUELTAS"])))
            self.assertEqual(845, len(self.table_values(workbook["ZONAS"])))
            self.assertEqual(17, len(self.table_values(workbook["SEMANAS"])))
            self.assertEqual(112, len(self.table_values(workbook["SALUD DIARIA"])))
            self.assertEqual(29, len(self.table_values(workbook["EQUIPAMIENTO"])))
            measures = workbook["MEDIDAS"]
            self.assertEqual(2, len(measures.tables))
            from openpyxl.utils.cell import range_boundaries
            composition_bounds = range_boundaries(
                measures.tables["TablaComposicion"].ref
            )
            pressure_bounds = range_boundaries(
                measures.tables["TablaPresionArterial"].ref
            )
            self.assertEqual(18, composition_bounds[3] - composition_bounds[1])
            self.assertEqual(7, pressure_bounds[3] - pressure_bounds[1])
            self.assertGreaterEqual(measures.column_dimensions["A"].width, 19)
            self.assertIsInstance(
                measures.cell(pressure_bounds[1] + 1, 1).value,
                datetime,
            )
            activity_rows = self.table_values(workbook["ACTIVIDADES"])
            sports = {}
            for row in activity_rows:
                sports[row["Deporte"]] = sports.get(row["Deporte"], 0) + 1
            self.assertEqual(60, sports["Carrera"])
            self.assertEqual(1, sports["Carrera por montaña"])
            self.assertEqual(16, sports["Ciclismo"])
            self.assertEqual(1, sports["Fuerza"])
            self.assertEqual(14, sports["Caminar"])
            running_km = sum(
                row["Distancia (km)"]
                for row in activity_rows
                if row["Deporte"] in {"Carrera", "Carrera por montaña"}
            )
            self.assertAlmostEqual(327.5796, running_km, places=4)
            self.assertEqual(6, len(self.table_values(workbook["DIARIO"])))
        finally:
            workbook.close()

    def test_visible_structure_is_human_and_technical_sheets_are_hidden(self):
        workbook = self.open_book(data_only=False)
        try:
            visible = [
                sheet.title for sheet in workbook.worksheets
                if sheet.sheet_state == "visible"
            ]
            self.assertEqual([
                "INICIO", "RESUMEN", "SEMANAS", "ACTIVIDADES",
                "INTERVALOS", "VUELTAS", "ZONAS", "SALUD DIARIA",
                "HÁBITOS", "MEDIDAS", "EQUIPAMIENTO", "DIARIO",
                "CALIDAD DATOS", "AYUDA",
            ], visible)
            hidden = [
                sheet.title for sheet in workbook.worksheets
                if sheet.sheet_state == "hidden"
            ]
            self.assertTrue(hidden)
            self.assertTrue(all(
                name.startswith("TÉCNICO - ") or name == "DATOS POR SEGUNDO"
                for name in hidden
            ))
            self.assertIn("TÉCNICO - ACTIVIDADES", hidden)
            self.assertIn("TÉCNICO - MAPEO", hidden)
            for sheet in workbook.worksheets:
                if sheet.sheet_state != "visible":
                    continue
                self.assertTrue(any(
                    cell.value is not None
                    for row in sheet.iter_rows()
                    for cell in row
                ), sheet.title)
                for table in sheet.tables.values():
                    min_col, min_row, max_col, _ = __import__(
                        "openpyxl.utils.cell", fromlist=["range_boundaries"]
                    ).range_boundaries(table.ref)
                    for column in range(min_col, max_col + 1):
                        header = str(sheet.cell(min_row, column).value or "")
                        self.assertNotIn("_", header)
                        self.assertNotIn(".", header)
            self.assertLessEqual(workbook["ACTIVIDADES"].max_column, 30)
            self.assertLessEqual(workbook["SEMANAS"].max_column, 35)
        finally:
            workbook.close()

    def test_dates_durations_paces_percentages_and_missing_values_are_real(self):
        workbook = self.open_book(data_only=False)
        try:
            activities = workbook["ACTIVIDADES"]
            headers = {cell.value: cell.column for cell in activities[1]}
            self.assertIsInstance(
                activities.cell(2, headers["Fecha"]).value,
                (datetime,),
            )
            self.assertNotIsInstance(
                activities.cell(2, headers["Duración"]).value,
                str,
            )
            self.assertEqual(
                "[m]:ss",
                activities.cell(2, headers["Ritmo medio (min/km)"]).number_format,
            )
            self.assertIsNone(
                activities.cell(2, headers["Potencia normalizada (W)"]).value
            )

            health = workbook["SALUD DIARIA"]
            health_headers = {cell.value: cell.column for cell in health[1]}
            self.assertEqual(0, health.cell(2, health_headers["Pasos"]).value)
            self.assertIsNone(
                health.cell(2, health_headers["Puntuación del sueño"]).value
            )
            self.assertIsInstance(
                health.cell(2, health_headers["Inicio del sueño"]).value,
                datetime,
            )

            zones = workbook["ZONAS"]
            zone_headers = {cell.value: cell.column for cell in zones[1]}
            percentage = zones.cell(2, zone_headers["Porcentaje"])
            self.assertGreaterEqual(percentage.value, 0)
            self.assertLessEqual(percentage.value, 1)
            self.assertEqual("0.0%", percentage.number_format)

            quality = workbook["CALIDAD DATOS"]
            quality_rows = self.table_values(quality)
            sleep_quality = next(
                row for row in quality_rows
                if row["Métrica"] == "Duración del sueño"
            )
            self.assertIsInstance(sleep_quality["Media"], timedelta)
            metric_column = {
                cell.value: cell.column for cell in quality[1]
            }
            self.assertEqual(
                "[h]:mm:ss",
                quality.cell(2, metric_column["Media"]).number_format,
            )
        finally:
            workbook.close()

    def test_no_visible_json_formula_or_formula_error(self):
        workbook = self.open_book(data_only=False)
        try:
            formula_errors = {"#REF!", "#DIV/0!", "#VALUE!", "#N/A"}
            for sheet in workbook.worksheets:
                if sheet.sheet_state != "visible":
                    continue
                for row in sheet.iter_rows():
                    for cell in row:
                        self.assertNotEqual("f", cell.data_type, f"{sheet.title}!{cell.coordinate}")
                        self.assertNotIn(cell.value, formula_errors)
                        if not isinstance(cell.value, str):
                            continue
                        stripped = cell.value.strip()
                        if stripped.startswith(("{", "[")):
                            try:
                                decoded = json.loads(stripped)
                            except json.JSONDecodeError:
                                continue
                            self.assertNotIsInstance(decoded, (dict, list))
        finally:
            workbook.close()

    def test_translations_session_types_and_microactivities_are_coherent(self):
        workbook = self.open_book(data_only=False)
        try:
            forbidden = {
                "strong", "trail_running", "trail running", "interval",
                "other", "none", "uncategorized",
            }
            for sheet in workbook.worksheets:
                if sheet.sheet_state != "visible":
                    continue
                for row in sheet.iter_rows():
                    for cell in row:
                        if isinstance(cell.value, str):
                            self.assertNotIn(
                                cell.value.strip().casefold(),
                                forbidden,
                                f"{sheet.title}!{cell.coordinate}",
                            )

            activities = self.table_values(workbook["ACTIVIDADES"])
            brief = [
                row for row in activities
                if row["Estado del registro"] == "Registro muy breve"
            ]
            self.assertEqual(2, len(brief))
            uncategorized_name = self.model["activities"][2]["name"]
            uncategorized = next(
                row for row in activities if row["Actividad"] == uncategorized_name
            )
            self.assertEqual("Sin clasificar", uncategorized["Tipo de sesión"])

            summary = workbook["RESUMEN"]
            indicators = {}
            for row in range(1, summary.max_row + 1):
                indicators[summary.cell(row, 1).value] = summary.cell(row, 2).value
                indicators[summary.cell(row, 4).value] = summary.cell(row, 5).value
            self.assertEqual(59, indicators["Sesiones de carrera"])
            self.assertEqual(
                self.model["period_summary"]["training"]["days_with_any_training"] - 1,
                indicators["Días entrenados"],
            )
            expected_load = sum(
                activity.get("training_load") or 0
                for activity in self.model["activities"]
                if not (
                    activity.get("duration_s") < 60
                    and activity.get("distance_m") < 100
                    and any(
                        token in str(activity.get("sport") or "").casefold()
                        for token in ("run", "cycl", "bike")
                    )
                )
            )
            self.assertEqual(expected_load, indicators["Carga Garmin total"])
            weekly = self.table_values(workbook["SEMANAS"])
            self.assertEqual(90, sum(row["Sesiones totales"] for row in weekly))

            quality_text = "\n".join(
                str(cell.value)
                for row in workbook["CALIDAD DATOS"].iter_rows()
                for cell in row if cell.value is not None
            )
            self.assertIn("2 registros muy breves", quality_text)
            self.assertIn("no cuentan en sesiones", quality_text)
        finally:
            workbook.close()

    def test_intervals_journal_dates_and_collapsed_columns(self):
        workbook = self.open_book(data_only=False)
        try:
            intervals = self.table_values(workbook["INTERVALOS"])
            short = next(row for row in intervals if row["Distancia (km)"] == 0.05)
            self.assertEqual("Intervalo", short["Tipo de intervalo"])
            self.assertEqual("Intervalo", short["Nivel"])
            for header in (
                "Ritmo medio (min/km)", "Ritmo en movimiento (min/km)",
                "Mejor ritmo (min/km)",
                "Ritmo ajustado por pendiente (min/km)",
            ):
                self.assertIsNone(short[header])

            journal = self.table_values(workbook["DIARIO"])
            self.assertEqual(6, len(journal))
            self.assertTrue(all(
                row["Nota"] or row["Tipo de sesión previsto"] for row in journal
            ))
            self.assertEqual("Prueba", journal[0]["Tipo de sesión previsto"])
            mismatches = [row for row in journal if row["Aviso"]]
            self.assertEqual(1, len(mismatches))
            self.assertNotEqual(
                mismatches[0]["Fecha del diario"],
                mismatches[0]["Fecha de la actividad"],
            )

            primary = {
                "ACTIVIDADES": {
                    "Fecha", "Actividad", "Deporte", "Tipo de sesión",
                    "Estado del registro", "Distancia (km)", "Duración",
                    "Ritmo medio (min/km)", "FC media (lpm)", "Carga Garmin",
                    "Esfuerzo percibido (1–10)", "Sensación", "Equipamiento",
                },
                "SEMANAS": {
                    "Semana", "Estado de la semana", "Sesiones totales",
                    "Días entrenados", "Carrera (km)", "Tiempo corriendo",
                    "Tirada más larga (km)", "Carga Garmin",
                    "Esfuerzo percibido medio", "Sueño medio",
                    "VFC nocturna (ms)", "Pulso en reposo (lpm)",
                },
                "SALUD DIARIA": {
                    "Fecha", "Pasos", "Pulso en reposo (lpm)", "Estrés medio",
                    "Body Battery máximo", "Sueño total",
                    "Puntuación del sueño", "VFC nocturna media (ms)",
                },
            }
            for sheet_name, expected_visible in primary.items():
                sheet = workbook[sheet_name]
                visible_headers = {
                    cell.value for cell in sheet[1]
                    if not sheet.column_dimensions[cell.column_letter].hidden
                }
                self.assertEqual(expected_visible, visible_headers)
                self.assertTrue(any(
                    dimension.hidden and dimension.outlineLevel == 1
                    for dimension in sheet.column_dimensions.values()
                ))
        finally:
            workbook.close()

    def test_visible_quality_dates_identifiers_and_weight_axis_are_friendly(self):
        workbook = self.open_book(data_only=False)
        try:
            quality_text = "\n".join(
                str(cell.value)
                for row in workbook["CALIDAD DATOS"].iter_rows()
                for cell in row if cell.value is not None
            )
            for technical in (
                "get_sleep_data", "get_hrv_data", "dailySleepDTO",
                "hrvSummary", "gear_type", "lap_type", "session_type",
            ):
                self.assertNotIn(technical.casefold(), quality_text.casefold())
            self.assertNotRegex(quality_text, r"\b\d{4}-\d{2}-\d{2}\b")

            for sheet in workbook.worksheets:
                if sheet.sheet_state != "visible":
                    continue
                for table in sheet.tables.values():
                    from openpyxl.utils.cell import range_boundaries
                    min_col, min_row, max_col, max_row = range_boundaries(table.ref)
                    for column in range(min_col, max_col + 1):
                        header = str(sheet.cell(min_row, column).value or "")
                        dimension = sheet.column_dimensions[
                            sheet.cell(min_row, column).column_letter
                        ]
                        if "ID" in header:
                            self.assertTrue(dimension.hidden, f"{sheet.title}: {header}")
                        if dimension.hidden or not any(
                            word in header for word in ("Fecha", "Desde", "Hasta")
                        ):
                            continue
                        for row in range(min_row + 1, max_row + 1):
                            cell = sheet.cell(row, column)
                            if cell.value is not None:
                                self.assertIn("dd/mm", cell.number_format.casefold())

            hidden_technical = {
                sheet.title for sheet in workbook.worksheets
                if sheet.sheet_state == "hidden"
                and sheet.title.startswith("TÉCNICO - ")
            }
            self.assertEqual({
                "TÉCNICO - ACTIVIDADES",
                "TÉCNICO - MODELO",
                "TÉCNICO - MAPEO",
            }, hidden_technical)

            measures = workbook["MEDIDAS"]
            weight_chart = measures._charts[0]
            self.assertEqual(
                "'MEDIDAS'!$A$2:$A$19",
                weight_chart.series[0].cat.numRef.f,
            )
            self.assertEqual("dd/mm", weight_chart.x_axis.numFmt.formatCode)
            self.assertFalse(weight_chart.x_axis.delete)
            self.assertEqual(
                "'RESUMEN'!$N$2:$N$18",
                workbook["RESUMEN"]._charts[0].series[0].cat.numRef.f,
            )
        finally:
            workbook.close()

    def test_four_complete_weeks_compare_two_against_two(self):
        weeks = []
        for index in range(4):
            weeks.append({
                "iso_week": f"2026-W{index + 1:02d}",
                "status": "complete",
                "training_sessions_total": 2,
                "days_with_any_training": 2,
                "running_sessions": 2,
                "running_distance_m": 20_000 + index * 1_000,
                "running_duration_s": 7_200,
                "longest_run_distance_m": 12_000,
                "garmin_training_load_total": 100,
                "session_rpe_load_total": 200,
            })
        model = {
            "export_metadata": {"schema_version": SCHEMA_VERSION},
            "weekly_timeline": weeks,
            "period_summary": {},
            "data_quality": {},
        }
        with tempfile.TemporaryDirectory() as directory:
            output = Path(directory) / "cuatro_semanas.xlsx"
            render_xlsx(model, output)
            workbook = load_workbook(output, data_only=False)
            try:
                values = {
                    str(cell.value)
                    for row in workbook["RESUMEN"].iter_rows()
                    for cell in row if cell.value is not None
                }
                self.assertIn(
                    "Comparación de 2 semanas completas con las 2 anteriores",
                    values,
                )
                self.assertIn("2 semanas anteriores", values)
                self.assertIn("2 semanas recientes", values)
            finally:
                workbook.close()

        self.assertEqual("3.3.1", SCHEMA_VERSION)

    def test_user_names_help_tables_charts_and_visual_settings(self):
        workbook = self.open_book(data_only=False)
        try:
            activities = self.table_values(workbook["ACTIVIDADES"])
            self.assertEqual("Sesión ficticia 1", activities[0]["Actividad"])
            gear = self.table_values(workbook["EQUIPAMIENTO"])
            self.assertEqual("Mis Zapatillas Ñandú", gear[0]["Nombre"])
            help_rows = self.table_values(workbook["AYUDA"])
            self.assertGreater(len(help_rows), 100)
            self.assertTrue(all(row["Qué significa"] for row in help_rows))
            self.assertFalse(any(
                "Campo del modelo semántico" in row["Qué significa"]
                for row in help_rows
            ))
            self.assertEqual(3, len(workbook["RESUMEN"]._charts))
            self.assertEqual(1, len(workbook["MEDIDAS"]._charts))
            self.assertTrue(all(
                chart.legend is None
                for chart in workbook["RESUMEN"]._charts[:2]
            ))
            self.assertIsNone(workbook["MEDIDAS"]._charts[0].legend)
            self.assertFalse(workbook["INICIO"].sheet_view.showGridLines)
            self.assertFalse(workbook["RESUMEN"].sheet_view.showGridLines)
            for name in (
                "SEMANAS", "ACTIVIDADES", "INTERVALOS", "VUELTAS",
                "ZONAS", "SALUD DIARIA", "EQUIPAMIENTO", "DIARIO",
                "CALIDAD DATOS", "AYUDA",
            ):
                self.assertTrue(workbook[name].tables, name)
                self.assertIsNotNone(workbook[name].freeze_panes, name)
            for name in ("ACTIVIDADES", "INTERVALOS", "VUELTAS", "EQUIPAMIENTO", "DIARIO"):
                sheet = workbook[name]
                hidden_columns = [
                    dimension
                    for dimension in sheet.column_dimensions.values()
                    if dimension.hidden
                ]
                self.assertTrue(hidden_columns, name)

            quality_text = "\n".join(
                str(cell.value)
                for row in workbook["CALIDAD DATOS"].iter_rows()
                for cell in row
                if cell.value is not None
            )
            self.assertNotIn("Data Quality.coverage", quality_text)
            self.assertNotIn("Valores aún sin traducción específica", quality_text)
            self.assertIn("Revisa la cobertura de datos", quality_text)
            self.assertIn("10/04/2026", quality_text)

            help_units = {
                row["Unidad"] for row in help_rows if row["Hoja"] == "INICIO"
            }
            self.assertNotIn("Según el campo", help_units)
        finally:
            workbook.close()

    def test_workbook_xml_is_valid_and_contains_no_personal_fixture(self):
        self.assertGreater(self.output.stat().st_size, 10_000)
        with zipfile.ZipFile(self.output) as archive:
            self.assertIsNone(archive.testzip())
            workbook_xml = archive.read("xl/workbook.xml").decode("utf-8")
            self.assertNotIn("\\users\\", workbook_xml.casefold())
            self.assertNotIn(".garminconnect", workbook_xml.casefold())
        workbook = self.open_book(data_only=False)
        try:
            start_values = {
                cell.value
                for row in workbook["INICIO"].iter_rows()
                for cell in row
                if isinstance(cell.value, str)
            }
            self.assertTrue(any(
                XLSX_PRESENTATION_VERSION in value for value in start_values
            ))
        finally:
            workbook.close()

    def test_large_series_are_omitted_only_from_xlsx_and_reported(self):
        samples = [[1.0]] * 25_001
        model = {
            "export_metadata": {
                "schema_version": SCHEMA_VERSION,
                "export_status": "completed",
            },
            "activities": [{
                "activity_ref": "activity_0123456789ab",
                "activity_series": {
                    "metric_descriptors": [{
                        "field": "elapsed_duration_raw",
                        "source_field": "sumElapsedDuration",
                        "source_unit": "second",
                    }],
                    "samples": samples,
                },
            }],
            "data_quality": {},
        }
        with tempfile.TemporaryDirectory() as directory:
            output = Path(directory) / "series_grandes.xlsx"
            render_xlsx(model, output)
            workbook = load_workbook(output, data_only=False)
            try:
                self.assertNotIn("DATOS POR SEGUNDO", workbook.sheetnames)
                quality_text = "\n".join(
                    str(cell.value)
                    for row in workbook["CALIDAD DATOS"].iter_rows()
                    for cell in row
                    if cell.value is not None
                )
                self.assertIn("25.001 muestras", quality_text)
                self.assertIn("TXT conserva las series completas", quality_text)
            finally:
                workbook.close()
        self.assertEqual(25_001, len(samples))


if __name__ == "__main__":
    unittest.main()
