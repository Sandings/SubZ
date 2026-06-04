# SubZ Test Report (as of 2026-05-07)

## 1. Scope
This report summarizes the major test coverage, outcomes, fixes, known risks, and follow-up regression recommendations for SubZ from development/integration to Unraid deployment.

Cutoff time: 2026-05-07 21:38 (Asia/Shanghai)

## 2. Test Environment
| Item | Details |
| --- | --- |
| Project path | `D:\Projects\SubZ` |
| Target platform | Emby (Unraid Docker) |
| Remote deployment endpoint | Unraid connection configured in the local `deploy_info.md` file |
| Plugin deployment path | `/mnt/user/DockerFile/emby/plugins/SubZ.Plugin.dll` |
| Default status page URL | `http://localhost:18123/subz-status.html` |
| Primary model provider | DeepSeek-compatible LLM API |
| Default model | `deepseek-v4-flash` |

## 3. Build & Deployment Tests
| Test item | Result | Evidence |
| --- | --- | --- |
| Release build | Passed (0 warnings / 0 errors) | `dotnet build ... -c Release` output (2026-05-07) |
| DLL artifact generation | Passed | `D:\Projects\SubZ\src\SubZ.Plugin\bin\Release\netstandard2.0\SubZ.Plugin.dll` |
| Unraid overwrite deployment | Passed | Remote file: `/mnt/user/DockerFile/emby/plugins/SubZ.Plugin.dll` |
| Emby container restart | Passed | `docker restart embyserver` |

## 4. Functional Test Summary
| Module | Test coverage | Result | Current conclusion |
| --- | --- | --- | --- |
| Config page | Native page access, field rendering, save flow | Passed | Uses Emby-native style with bilingual field titles |
| Manual execution | Manual target file / folder | Passed | Manual trigger works; recursive folder mode is supported |
| Auto-ingest trigger | ItemAdded hook + startup grace period + dedupe window | Implemented and code-reviewed | Feature exists; long-run stability regression is recommended |
| Status page | External HTML + API read for queue/runtime/logs | Passed (404 issue fixed) | External status page is available |
| Runtime controls | Pause / resume / stop | Partially passed | Historical "auto-resume after pause" issue was fixed; keep observing |
| Output format | `srt` / `ass`, bilingual merge | Passed | ASS font and color options are supported |
| Subtitle source | External-first + embedded extraction (ffmpeg/ffprobe) | Passed | Startup path validation and warnings added |
| Logging system | Dedicated runtime log, rotation, retention, debug mode | Passed | Dedicated log file + status-page log fetch supported |

## 5. Subtitle Quality & Retry Tests
### 5.1 Observed Issues
| Issue type | Symptom | Status |
| --- | --- | --- |
| Misalignment / shifted lines | Mid-segment line-to-time mismatch in some episodes | Fixed in detection/alignment logic; continued regression required |
| Missing translation at tail segments | Some ranges remained untranslated | Retry and reason logging improvements applied |
| High tail-retry overhead | Significant token increase | Root cause identified as `unchanged_source_and_protected` |

### 5.2 Quantitative Results (from runtime logs)
Source files:
- `D:\Projects\SubZ\tmp\latest-task-tail-retry-analysis.md`
- `D:\Projects\SubZ\tmp\last-6-files-tail-retry-analysis.md`
- `D:\Projects\SubZ\tmp\rebellious-women-season1-tail-retry-report.md`

Key metrics:
- Total tokens (latest 6 files): `423,880`
- Tail-retry tokens: `82,039` (`19.35%`)
- Dominant aggregated reason: `unchanged_source_and_protected = 1084`

Example (latest S01E02 task):
- Total tokens: `76,509`
- Tail-retry tokens: `9,557` (`12.49%`)
- Retry ranges: 5 ranges (with exact cue time windows)

## 6. Targeted Optimizations Completed (and Deployed)
| Optimization | Status | Notes |
| --- | --- | --- |
| Lower default temperature | Completed | Default `temperature` changed from `0.2` to `0.1` |
| Stronger base translation prompt | Completed | Enforced "must translate, no source copy-through, keep markers/count/order" |
| Specialized retry prompt for unchanged lines | Completed | Tail-retry switches to `RetryUnchanged` mode by failure reason |
| Enhanced retry observability | Completed | Logs now include reason summary, retry range, cue time window, prompt mode |

## 7. Security & Engineering Quality Review Summary
| Item | Result | Handling |
| --- | --- | --- |
| Plaintext secret risk | Identified | Reminder issued: do not commit secret files under `tmp` |
| Retryability classification | Improved | Non-retryable exceptions are no longer retried blindly |
| God-class architecture risk | Identified | Refactor roadmap started; further decomposition needed |
| ffmpeg/ffprobe availability | Improved | Startup validation and explicit warning logs added |

## 8. Known Limitations & Pending Regression Items
| Item | Status |
| --- | --- |
| Whether "pause then auto-resume" is fully eliminated | Requires more long-running task validation |
| Net quality/token gain from new prompt strategy | Needs new run data for before/after comparison |
| Stability across languages and source types (external/embedded) | Requires broader sample regression |
| OCR path (SUP -> OCR -> translation) | Not included in current execution pipeline |

## 9. Recommended Next Regression Round
1. Run 3 consecutive episodes from the same show and verify whether tail-retry ratio continues to decrease.
2. Compare playback consistency between `srt` and `ass` outputs.
3. Under high-load recursive folder runs, verify pause/resume/stop state transitions.
4. Compare performance and log volume with Debug mode on vs off.

## 10. Conclusion
SubZ currently has a production-ready baseline in deployment, execution, observability, and manual control.

At this stage, the main cost driver remains tail retries caused by unchanged lines. A dedicated prompt strategy has been implemented and deployed. The next real-run dataset will determine whether we should proceed with finer-grained selective resend strategies.

## 11. Detailed Data Appendix (Token Usage & Exceptions)
### 11.1 Per-run metrics for the latest 6 tasks (from latest log)
| File | Total | Prompt | Completion | Cues | Tail-retry Total | Tail-retry Ratio | Main retry reason |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | --- |
| S01E06 | 66,623 | 35,048 | 31,575 | 971 | 13,095 | 19.66% | unchanged_source_and_protected=182 |
| S01E05 | 71,313 | 37,272 | 34,041 | 1,075 | 16,960 | 23.78% | unchanged_source_and_protected=176 |
| S01E03 | 66,186 | 34,721 | 31,465 | 961 | 10,638 | 16.07% | unchanged_source_and_protected=181 |
| S01E01 | 68,026 | 35,547 | 32,479 | 930 | 17,501 | 25.73% | unchanged_source_and_protected=197 |
| S01E04 | 75,223 | 39,349 | 35,874 | 1,031 | 14,288 | 18.99% | unchanged_source_and_protected=221 |
| S01E02 | 76,509 | 40,123 | 36,386 | 1,225 | 9,557 | 12.49% | unchanged_source_and_protected=127 |

Aggregates:
- Total tokens (latest 6 tasks): `423,880`
- Tail-retry tokens: `82,039`
- Tail-retry ratio: `19.35%`

### 11.2 Historical 6-task window (from older log window)
| File | Total | Prompt | Completion | Cues | Tail-retry Total | Tail-retry Ratio |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| S01E01 | 79,719 | 42,256 | 37,463 | 930 | 35,137 | 44.08% |
| S01E03 | 55,562 | 29,125 | 26,437 | 961 | 0 | 0.00% |
| S01E06 | 65,787 | 34,763 | 31,024 | 971 | 14,276 | 21.70% |
| S01E05 | 66,163 | 34,472 | 31,691 | 1,075 | 13,510 | 20.42% |
| S01E04 | 75,708 | 39,606 | 36,102 | 1,031 | 14,066 | 18.58% |
| S01E02 | 86,936 | 47,041 | 39,895 | 1,225 | 23,647 | 27.20% |

Aggregates:
- Total tokens: `429,875`
- Tail-retry tokens: `100,636`
- Tail-retry ratio: `23.41%`

### 11.3 Exception / Warning Event Counters
Note: "exceptions" here include both hard failures and quality-warning events.

`subz-runtime-20260507.latest.log`:
- `HTTP non-200 responses`: `0`
- `Healing failed segments`: `35`
- `Retry range`: `65`
- `Marker mismatch detected in batch lines`: `23`
- `Marker mismatch detected in retry lines`: `4`
- `remain after retries` (still unresolved after retries): `1`

`subz-runtime-20260507.log`:
- `HTTP non-200 responses`: `0`
- `Healing failed segments`: `20`
- `Retry range`: `47`
- `Marker mismatch detected in batch lines`: `11`
- `Marker mismatch detected in retry lines`: `2`
- `remain after retries`: `0`

### 11.4 Data-backed Conclusions
- The dominant cost issue is not API transport failure (HTTP non-200 = 0), but frequent quality-recovery triggers (`unchanged` + marker mismatch).
- Latest 6-task tail-retry ratio is `19.35%`, lower than the historical `23.41%`, but still high.
- In the next round, validate whether the new specialized retry prompt for unchanged lines reduces `Healing failed segments` and `Retry range` counts.
