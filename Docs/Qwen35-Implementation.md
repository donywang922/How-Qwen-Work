# Qwen GPU 世界实现与维护

PC 使用 Qwen3.5 0.8B 的完整 24 层文本网络。权重按 ShaderGPT 的 Q4 格式保存为只读贴图，计算状态使用 FP32 RenderTexture，Udon 通过 `VRCGraphics.Blit` 执行烘焙图，并以异步 GPU 回读取回 token。视觉编码器未导入世界。248,320 行嵌入包含填充行，分词器实际定义 248,077 个 token。

## 代码职责

| 目录或组件 | 职责 |
|---|---|
| Runtime/QwenTextureRunner | 849 算子调度、89 个状态缓冲、暂停和异步 token 回读 |
| Runtime/QwenWorldController | 输入、共享对话、生成生命周期与执行端权限 |
| Runtime/QwenExhibitState | 展示状态、核心高亮和许愿事件同步 |
| Runtime/QwenComputeDisplay | 本地算子读写、核心和六条动作的材质数据 |
| Runtime/QwenSocialUI、QwenKeyboard | 80 个只读面板分配与渐显展开、带角速度的平滑键盘跟随和小尺寸锚定输入屏 |
| Runtime/QwenLocalMenu | Tab、双击板机、面前菜单和许愿手势 |
| Runtime/QwenLocalization | 本地四语文本表、界面切换与帮助 |
| Runtime/QwenObservationPlatform | 本地升降平台、乘坐跟随、掉落复位和安全回收 |
| Editor/Build | 统一路径、模型处理、平台剔除和 SDK 本地打包 |
| Editor/Setup | 创建主世界、重装展厅 |
| Editor/Environment | 地板、天空、外环、立牌与烘焙光照 |
| Editor/Gallery | 静态图集、原图、CRT 展示、布局和读写几何 |
| Editor/UI | 投影键盘、玻璃界面和菜单安装 |
| Editor/Validation | GPU、ClientSim、原生测试场景和渲染验证 |
| Editor/Maintenance | 依赖检查、归档清理与资源审计 |
| Shaders/Inference、Display、Environment、UI | 推理、数据展示、场景材质和界面着色器 |
| Tools/Qwen35 | Python 资源生成、模型准备、测试和 PowerShell 统一入口 |

模型导出算法、Q4 编码、权重和生成规则保持一致。两个会写入临时 uniform 的材质通过禁用 Renderer 的 `material` 接口取得独立运行时实例，不再把位置、序号写回预制体；Udon 不支持直接构造 Material。这两个载体不提交绘制，也不复制权重贴图。旧单张快照与热力图展示链已移除，避免每个算子额外复制一张已不可见的状态图。静态图远处使用合并图集，靠近才启用原始纹理；89 张 CRT 保持原有本地近距离浮雕展示。

推理进度由控制器统一格式化，输入屏、菜单和聊天底部复用。显示贴图时，执行端约每 0.5 秒向展示状态提交已处理上下文、上下文总数、输出 token、当前算子、算子总数、当前层六个整数；接收端验证发送者为控制器所有者，不接收资源 ID 或贴图数据。百分比区分上下文整体和当前 token，不估算未知的回答长度。隐藏贴图时停发详细进度与层掩码变化，只提交运行、暂停、停止及模式/所有者切换状态；观看端显示简短状态，执行端仍可读取本地真实进度。聊天文本同步保持原有方式。

隐藏贴图时核心播放本地预设循环：每 0.1 秒推进一层，保留一层尾迹，24 层约 2.4 秒一轮；暂停冻结，停止熄灭。动画不代表真实算子进度，也不要求客户端之间同相位。切回展示模式会立即请求真实层及进度快照，不等待原有发送间隔。隐藏模式的接收端也拒绝迟到的详细进度，并将层掩码归零。`QwenCoreSyncValidation.ValidateBatch` 用真实 Udon 与 ClientSim 模拟状态检查发送抑制、动画及模式切换，不执行模型算子，也不测量真实多人网络带宽。

## 单主场景与平台

`Assets/QwenWorld/Scenes/QwenLaboratory.unity` 是唯一编辑源，沿用原 PC 场景 GUID。EditorBuildSettings 只保留它。`QwenPlatformBuild` 在 Android 与 iOS 构建时使用相同的观看端处理：清空推理引用、禁用 tokenizer 与输入、移除模型和贴图展示，保留相同的网络对象及 ID。需要生成观看副本时，副本位于 `Assets/QwenWorld/Generated/Build`，每次重新生成。

移动端剔除还会清理 SDK 描述器的动态材质及预制体缓存，并重绑定复制后的烘焙光照元数据。否则即使删除模型物体，缓存或光照数据中的场景引用仍可能间接带入整套模型。构建回调检查实际构建场景的模型依赖为零，残留时中止构建。

`QwenWorldBuild` 通过 SDK 的公开 `Build` API 打本地包；测试场景由主场景复制并加入测试脚本，正常世界不携带回归脚本。所有入口均不调用上传 API。公开 SDK 面板直接构建时的平台处理另由 `IProcessSceneWithReport` 执行；其实际结果见验证报告。

`VerifyIOS` 命令（Unity 关闭时运行）或 iOS 平台下的 `Qwen World > Validation > Build iOS viewer from main (no upload)` 菜单，通过主场景执行同一构建回调，检查包小于 100 MB、主场景仍启用 PC 推理，不会上传。

## 数据与资源

- `Models/Qwen35-Full-Q4`：完整词表权重、算子材质、状态缓冲、预制体和参考数据。不要对数值贴图开启有损压缩、sRGB 或双线性过滤。
- `Generated/Gallery/Qwen35-Full-Q4`：图集、展示网格和材质，重装展厅可再生成。
- `Environment`：运行时材质、烘焙网格和原图。外环 FBX、五张源贴图、立牌源图保留用于重建。
- `Scenes/QwenLaboratory`：主场景烘焙光照，不按无代码引用资源处理。
- `Fonts`、`Licenses`：CJK 字体及许可。第三方包由 Packages/VPM 管理，不混入自有资源清理。
- `Generated/Build`：平台及测试缓存，不是独立设计场景，可由构建流程重新生成。

外部 `QWEN_DATA_ROOT` 保存原始模型、导出、日志、构建和验证。默认项目内 `.qwen-work`，也可由环境变量或被忽略的 `.qwen-local.json` 指定。Python 的 `qwen_paths.py` 与编辑器的 `QwenProject` 各集中定义一次路径；资源生成的输入不再依赖聊天附件临时路径。Qwen 图标参考图保存在 `Tools/Qwen35/Source`。

## 回归与清理

根目录 README 列出统一入口。模型改动运行 Test、Smoke；交互或场景改动运行 Validate；资源清理后运行 Audit；材质改动补充 Preview。BuildAll 只构建 PC 与 Android 并恢复 Win64 主场景；Summary 单独收集已有本地报告，不要求历史基线。

Smoke 检查 GPU logits 误差、中英日韩精确 token 序列，以及暂停、单算子、单层、单 token。Validate 使用真实 Udon 和 GPU，但玩家和网络调用上下文是 ClientSim 合成数据，检查自动获取、持有者保留、距离交接、最近 PC 选择、快速跟随、长文本滚动裁切、共享状态、许愿、80 人面板池和观看端。Console 检查屏幕位于桌环内、输入屏高于键盘、深色背景与外环包围平台的几何关系，并输出实际 GPU 预览。Audit 检查脚本、网格、材质、布局遮挡、平台 ID 与模型依赖。

Observation 检查外围透明台阶、出生点、升降范围、菜单玻璃材质和天空环朝向，并输出预览。Validate 和 NativeTest 均检查平台乘坐、掉落隐藏和虚空回收，原生测试额外检查下降时跟随。平台使用本地运动学刚体与 MeshCollider；调整高度期间，以本地玩家脚下相对平台的距离判定乘坐，只对当前玩家应用垂直位移。未添加 VRCObjectSync，也没有同步高度变量。圆形地面共用一个 768 三角形网格，新增两个透明 Renderer，未新增相机、CRT 或逐帧贴图复制。

清理命令处理 `Assets/QwenWorld` 内资源，以及明确列出的未使用 VRC 默认模板场景。先取得主场景递归依赖，再保护编辑器、运行时、完整模型、字体许可、着色器、外环与立牌重建源；删除前复制资源及 meta 到外部归档。报告记录每个移出路径和字节数。迁移前完整快照和本次清理清单位于外部 `archive/refactor-20261123`、`archive/cleanup` 与 `validation/refactor`。

纹理预算只计算引用贴图和推理缓冲的有效数据载荷，不等于总显存；驱动、运行时副本、深度和帧缓冲另占内存。本地资源预算与运行报告保存在工作目录，报告是否覆盖当前代码需按其时间和范围判断。单客户端回归不能替代真实 PC／PC、PC／Android 网络及头显、Android 设备验收。

四语界面的 `QwenLocalization` 使用同步模式 None，没有联网语言变量。回复语言选择已删除；控制器不再提供 systemPrompt 字段，请求中完全没有 system 消息，只有用户／助手历史和当前问题，保留模型要求的 assistant 生成前缀。`QwenLocalizationBuilder` 可局部安装并重用同一字体与玻璃材质；完整场景安装也调用它，Android 从主场景复制同一界面与本地控制。`Localization` 命令只做相关编译和界面验证，不触发模型回归、烘焙或打包。

`world_reference.py` 的模板已改为 user-assistant-no-system-v1，但本次未运行它重算参考输出。此前四语精确 token 和原生客户端推理报告属于旧输入模板，不作为新模板的精确输出证据；后续需要运行推理回归时先刷新参考序列。

## 源码分发

自有代码及外环使用 Apache-2.0。千问 AI 立绘由 ZipZipPipe 发布于 Pixiv 148186519，保留原作“需署名、不可商用”的使用条件；抠图及表情处理另行记录，见 QwenGirl-Usage-Terms.md。该素材不属于 Apache-2.0，DeepSeek 专用的 CC BY-NC-SA 4.0 说明不套用到千问立绘。第三方许可单独保留。
模型 PNG／EXR、tokenizer JSON 及其 Assets 下的 `.meta` 不提交，完整导入配置与 GUID
保存在 Tools/Qwen35/model-payloads.lock.json。初始化先恢复资源，再编译 Udon，保留现成计算图与场景。
SDK 从 VCC 恢复；Assets 下的 SDK 工具副本和 SerializedUdonPrograms 缓存不分发。
Setup 已检查主场景不依赖被排除的 SDK 工具副本。没有重新烘焙、转换模型或运行推理。

旧的个人来源路径已从公开文档移除，原始记录保留在维护者的本地备份。主场景解除世界 ID 绑定；
原 ID 留在本地配置，可用菜单生成独立的本地发布副本。源码包通过候选文件过滤生成，
不初始化 Git、不连接 GitHub、不上传。完整步骤见根目录中英文 README。

`QwenInteractionPolish.ApplyBatch` 原位更新广告点击范围和会话布局：仅广告面透明 UI 可点击、键盘带阻尼、聊天框下移并渐显展开。保留家具位置，执行局部命中和动画检查，不运行推理、烘焙或构建。

前方菜单以玩家相对位置跟随移动，保持拖动期间的朝向；世界控制器显式启用 3 的跳跃力度。暂停连线使用独立的预览标记与历史时间戳，单步切换不清空上一算子的消散。`QwenPlatformInteractionFix.ApplyBatch` 提供不启动推理的局部 ClientSim 验证。

模型导入设置误改（如 SDK 的 8192 Auto Fix）可用 `Qwen World > Model > Restore original texture import settings`，按 `model-payloads.lock.json` 恢复模型纹理的 `.meta`，保留 GUID；只重导入被改变的设置，另核对嵌入贴图实际为 15520×4096、导入上限 16384。SDK 大贴图提示属于建议，模型数据纹理需保持原始尺寸与格式。
