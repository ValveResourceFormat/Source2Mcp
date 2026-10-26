# Particle packets

## GRPT

A generic report. The particle profiler sends it at the end of a measurement window. The console commands `particle_profile <systems|operators> <num_frames>` and `particle_profile_spike <systems|operators> <spike_threshold_ms>` start the window.

- Direction: S->C
- Version: 1

Payload: one KV3 object.

| Key             | Type             | Meaning                                                                                                                      |
| --------------- | ---------------- | ---------------------------------------------------------------------------------------------------------------------------- |
| report_title    | string           | `Particle System Perf` or `Particle Operator Perf`.                                                                          |
| report_type     | string           | `sheet`.                                                                                                                     |
| report_info     | array of string  | Text lines.                                                                                                                  |
| report_time     | number           | Time of the report.                                                                                                          |
| report_time_str | string           | Time as text.                                                                                                                |
| report_columns  | array of objects | One object for each column. Keys: `name`, `key`, `type` (for example `string` or `time_us`), `default_sort`, `max_expected`. |
| report_rows     | array of objects | One object for each row. The keys are the column keys.                                                                       |

The `Particle System Perf` report has these column keys: `total_sim`, `max_sim`, `avg_sim`, `max_instances`, `total_frames`, `max_particles`, `alloc_particles`, `total_render`, `max_render`, `avg_render`, `avg_num_tests`, `avg_num_traces`, `point_1` to `point_4`.
