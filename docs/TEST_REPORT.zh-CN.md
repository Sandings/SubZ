# SubZ 测试记录总报告（截至 2026-05-07）

## 1. 文档范围
本报告汇总 SubZ 插件从开发联调到 Unraid 部署阶段的主要测试内容、结果、已修复问题、遗留风险与后续回归建议。

统计截止时间：2026-05-07 21:38 (Asia/Shanghai)

## 2. 测试环境
| 项目 | 信息 |
| --- | --- |
| 插件项目路径 | `D:\Projects\SubZ` |
| 目标平台 | Emby（Unraid Docker） |
| 远程部署地址 | 本地 `deploy_info.md` 中配置的 Unraid 连接 |
| 插件部署位置 | `/mnt/user/DockerFile/emby/plugins/SubZ.Plugin.dll` |
| 状态页默认地址 | `http://localhost:18123/subz-status.html` |
| 主要模型提供方 | DeepSeek 兼容 LLM API |
| 默认模型 | `deepseek-v4-flash` |

## 3. 构建与部署测试
| 测试项 | 结果 | 证据 |
| --- | --- | --- |
| Release 构建 | 通过（0 warning / 0 error） | `dotnet build ... -c Release` 输出（2026-05-07） |
| DLL 产物生成 | 通过 | `D:\Projects\SubZ\src\SubZ.Plugin\bin\Release\netstandard2.0\SubZ.Plugin.dll` |
| Unraid 覆盖部署 | 通过 | 远程文件：`/mnt/user/DockerFile/emby/plugins/SubZ.Plugin.dll` |
| Emby 容器重启 | 通过 | `docker restart embyserver` |

## 4. 功能测试汇总
| 模块 | 测试内容 | 结果 | 当前结论 |
| --- | --- | --- | --- |
| 配置页 | 原生配置页可进入、字段显示与保存 | 通过 | 当前采用 Emby 原生风格，字段为双语标题 |
| 手动执行 | 手动目标文件、手动目标文件夹 | 通过 | 支持手动触发；手动文件夹模式已支持递归 |
| 自动入库触发 | ItemAdded Hook + 启动保护 + 去重窗口 | 已实现并经代码审查 | 功能存在，建议继续做长时间稳定性回归 |
| 状态页 | 外部 HTML 读取 API、展示队列/运行状态/日志 | 通过（曾出现 404 后已修复） | 状态页作为独立页面可用 |
| 状态控制 | 暂停/继续/停止 | 部分通过 | 历史出现“暂停后自动继续”问题，已修正逻辑，建议继续观察 |
| 输出格式 | `srt` / `ass` 输出、双语合并 | 通过 | 已支持 ASS 字体与颜色配置 |
| 字幕来源 | 外挂字幕优先、封装内字幕抽取（ffmpeg/ffprobe） | 通过 | 已增加路径启动检测与告警 |
| 日志系统 | 独立运行日志、轮转、保留天数、Debug 模式 | 通过 | 支持独立文件日志与状态页读取 |

## 5. 字幕质量与重试机制测试
### 5.1 已观测问题
| 问题类型 | 现象 | 处理状态 |
| --- | --- | --- |
| 错行/串行 | 个别剧集中段出现翻译与时间轴错位 | 已针对判定与对齐逻辑修复，需持续回归 |
| 尾段漏译 | 某些区间出现原文未翻译 | 已通过尾段重试和原因日志增强定位 |
| 大量尾段重试 | token 消耗明显上升 | 已定位主要原因为 `unchanged_source_and_protected` |

### 5.2 量化结果（来自运行日志）
数据来源文件：
- `D:\Projects\SubZ\tmp\latest-task-tail-retry-analysis.md`
- `D:\Projects\SubZ\tmp\last-6-files-tail-retry-analysis.md`
- `D:\Projects\SubZ\tmp\rebellious-women-season1-tail-retry-report.md`

关键统计：
- 最近 6 个文件总 token：`423,880`
- 其中尾段重试 token：`82,039`（`19.35%`）
- 主因聚合：`unchanged_source_and_protected = 1084`

示例（最近任务 S01E02）：
- 总 token：`76,509`
- 尾段重试 token：`9,557`（`12.49%`）
- 重试范围：5 段（含精确 cue 时间区间）

## 6. 已完成的针对性优化（并已部署）
| 优化项 | 状态 | 说明 |
| --- | --- | --- |
| 温度参数默认下调 | 已完成 | 默认 `temperature` 从 `0.2` 调整为 `0.1` |
| 通用翻译提示词强化 | 已完成 | 增强“必须翻译、禁止照抄、保持行标记与行数”约束 |
| 未变化行二次专用提示 | 已完成 | 尾段重试时按失败原因切换 `RetryUnchanged` 提示模式 |
| 重试日志增强 | 已完成 | 记录失败原因汇总、重试范围、cue 时间窗口、prompt 模式 |

## 7. 安全与工程质量测试/审查结论
| 项目 | 结果 | 处理 |
| --- | --- | --- |
| 明文密钥风险 | 已识别 | 已提醒避免将 `tmp` 中密钥文件提交仓库 |
| 可重试异常判定 | 已优化 | 非可重试异常不再无脑重试 |
| God class 风险 | 已识别 | 已进入拆分改造路线，需持续推进 |
| ffmpeg/ffprobe 可用性 | 已增强 | 启动时检测路径并写入明确告警 |

## 8. 已知限制与待回归项
| 项目 | 状态 |
| --- | --- |
| “暂停后自动继续”是否完全消失 | 需继续多轮长任务验证 |
| 新提示词策略对 token 与质量的净收益 | 需在新一轮任务后做前后对比 |
| 不同语言与不同字幕源（外挂/内封）的稳定性 | 需扩大样本回归 |
| OCR 路线（SUP -> OCR -> 翻译） | 尚未纳入当前版本执行链路 |

## 9. 回归建议（下一轮）
1. 用同一剧集连续跑 3 集，记录尾段重试占比是否持续下降。
2. 对比 `srt` 与 `ass` 两种输出在播放器端的显示一致性。
3. 在高负载场景（批量文件夹递归）验证暂停/继续/停止的状态正确性。
4. 在 Debug 日志开启/关闭两种模式下，对比性能与日志体积。

## 10. 结论
当前 SubZ 已具备可部署、可运行、可观测、可手动控制的生产可用基础能力。核心风险已从“不可定位”转为“可定位并可针对性优化”。

截至本报告，最主要的成本项仍是“未变化行触发的尾段重试”；针对该问题的专项提示策略已上线并部署，下一轮实跑数据将决定是否继续做分片重试与更细粒度重发策略。

## 11. 详细数据附录（Token 与异常）
### 11.1 最近 6 个任务逐条统计（来自 latest 日志）
| 文件 | Total | Prompt | Completion | Cues | 尾段重试 Total | 尾段重试占比 | 主要重试原因 |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | --- |
| S01E06 | 66,623 | 35,048 | 31,575 | 971 | 13,095 | 19.66% | unchanged_source_and_protected=182 |
| S01E05 | 71,313 | 37,272 | 34,041 | 1,075 | 16,960 | 23.78% | unchanged_source_and_protected=176 |
| S01E03 | 66,186 | 34,721 | 31,465 | 961 | 10,638 | 16.07% | unchanged_source_and_protected=181 |
| S01E01 | 68,026 | 35,547 | 32,479 | 930 | 17,501 | 25.73% | unchanged_source_and_protected=197 |
| S01E04 | 75,223 | 39,349 | 35,874 | 1,031 | 14,288 | 18.99% | unchanged_source_and_protected=221 |
| S01E02 | 76,509 | 40,123 | 36,386 | 1,225 | 9,557 | 12.49% | unchanged_source_and_protected=127 |

聚合：
- 最近 6 任务总 Token：`423,880`
- 尾段重试 Token：`82,039`
- 尾段重试占比：`19.35%`

### 11.2 历史 6 次任务统计（来自旧日志窗口）
| 文件 | Total | Prompt | Completion | Cues | 尾段重试 Total | 尾段重试占比 |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| S01E01 | 79,719 | 42,256 | 37,463 | 930 | 35,137 | 44.08% |
| S01E03 | 55,562 | 29,125 | 26,437 | 961 | 0 | 0.00% |
| S01E06 | 65,787 | 34,763 | 31,024 | 971 | 14,276 | 21.70% |
| S01E05 | 66,163 | 34,472 | 31,691 | 1,075 | 13,510 | 20.42% |
| S01E04 | 75,708 | 39,606 | 36,102 | 1,031 | 14,066 | 18.58% |
| S01E02 | 86,936 | 47,041 | 39,895 | 1,225 | 23,647 | 27.20% |

聚合：
- 总 Token：`429,875`
- 尾段重试 Token：`100,636`
- 尾段重试占比：`23.41%`

### 11.3 异常/告警事件计数
定义说明：此处“异常”包含系统失败与质量告警（并非全部是 HTTP/运行时异常）。

`subz-runtime-20260507.latest.log`：
- `HTTP 非 200 响应`：`0`
- `Healing failed segments`：`35`
- `Retry range`：`65`
- `Marker mismatch detected in batch lines`：`23`
- `Marker mismatch detected in retry lines`：`4`
- `remain after retries`（重试后仍残留）:`1`

`subz-runtime-20260507.log`：
- `HTTP 非 200 响应`：`0`
- `Healing failed segments`：`20`
- `Retry range`：`47`
- `Marker mismatch detected in batch lines`：`11`
- `Marker mismatch detected in retry lines`：`2`
- `remain after retries`：`0`

### 11.4 结论（基于数据）
- 当前阶段主要成本异常不是 API 失败（HTTP 非 200 为 0），而是翻译质量恢复链路频繁触发（unchanged + marker mismatch）。
- 近期 6 任务尾段重试占比 `19.35%`，较历史窗口 `23.41%` 有下降，但仍偏高。
- 下一轮应重点观察“未变化行二次专用提示”上线后，`Healing failed segments` 与 `Retry range` 的下降趋势。
