# How Qwen Work

在 VRChat 世界中以 GPU 数据贴图运行 Qwen3.5-0.8B 文本模型，展示真实权重、暂存状态和算子读写。
PC 本地推理，Android 观看共享聊天和核心状态。自有代码、立牌与外环素材采用 Apache-2.0；
第三方模型、字体、SDK 和天空素材保留各自许可，见 [NOTICE](NOTICE) 和 [第三方说明](THIRD_PARTY_NOTICES.md)。

## 第一次获取源码

需要 Unity **2022.3.22f1**、VRChat Creator Companion、Python **3.12**。
通过 VCC 安装或恢复 Worlds SDK **3.10.6**，版本记录在 `Packages/vpm-manifest.json`。
本仓库不分发 SDK 缓存，也不需要任何人的 SDK 登录信息。

模型 PNG／EXR 和生成的 tokenizer JSON 不进入 Git。其他场景、材质、几何、烘焙光照、
字体和美术资源保留。**先准备模型，再打开主场景**，在项目根目录运行：

```powershell
./Tools/Qwen35/Initialize-Qwen.ps1
./Tools/Qwen35/Invoke-Qwen.ps1 Setup
```

第一步自动创建 Python 环境、安装固定依赖、下载固定版本的原始权重和导出器，
量化转换后恢复贴图。默认使用 CPU，可用 `-TorchBackend cu128` 选择对应 CUDA 环境。
不会重新训练模型，也不会自动跑四语言推理回归。第一次下载和转换需要时间。
第二步检查资源 GUID、场景引用并编译 Udon，不重装场景、不烘焙、不推理、不上传。
也可在 Unity 中使用 `Qwen World > Setup > Initialize source checkout`。

已有相同版本导出时，可免下载和转换：

```powershell
./Tools/Qwen35/Initialize-Qwen.ps1 -FromExport 'E:/QwenExport/Qwen35-Full-Q4'
```

脚本先校验全部生成文件，再复制资源；Unity `.meta`／GUID 从仓库中的生成清单恢复，
不会因先打开 Unity 丢失孤立 `.meta` 而永久损坏引用。校验不符会停止，避免混用模型版本。

## 本地目录与构建

工作目录优先级：显式 `-DataRoot` → `QWEN_DATA_ROOT` → 被忽略的 `.qwen-local.json`
→ 项目内 `.qwen-work`。可复制 `qwen-local.example.json` 作为本地配置。
Unity 路径可用 `-UnityPath`／`QWEN_UNITY_PATH` 指定，也会尝试寻找对应版本的 Hub 安装。
没有固定 D 盘或个人用户名依赖。原始模型、Python 环境、缓存、导出、日志、报告和构建包均留在工作目录。

```powershell
./Tools/Qwen35/Invoke-Qwen.ps1 BuildPC
./Tools/Qwen35/Invoke-Qwen.ps1 BuildAndroid
./Tools/Qwen35/Invoke-Qwen.ps1 BuildAll
```

以上只生成本地包。BuildAll 不要求旧测试报告、历史归档、裁词模型或 Python 汇总环境；
完成后恢复 Win64 主场景。`Summary` 单独收集已有报告，缺少报告不会阻止构建。
日常修改按范围使用 Compile、Localization、Console、Observation 等局部检查。
需要推理回归时先执行 `Reference`，再执行 Smoke／Validate／NativeTest；参考序列由当前
无 system 消息的模板生成，不使用旧提示词下写死的 token。完整导出和 Bake／Rebuild 是高级重建流程，正常克隆后无需重跑。

## 使用与同步

靠近环桌自动获得空闲键盘，离开范围后自动交接。Tab 或同手双击板机打开个人菜单。
顶部切换中英日韩界面，问号打开帮助；语言只保留本次会话，聊天正文保留原文。
模型请求没有 system 消息、空 system 段或回复语言指令，只含用户／助手历史、当前问题及 assistant 生成前缀。
贴图显隐、聊天、进度和核心状态共享，CRT 数值与读写线仅执行端可见。
个人亚克力平台可升降 0–5.5 米，最低隐藏，掉落复位。

公开场景不绑定已发布世界。维护者可在本地配置保存 `worldBlueprintId`，再通过
`Qwen World > Build > Open local publish copy` 生成绑定原世界 ID 的本地发布副本，
随后自己在 SDK 面板完成发布。副本被 Git 忽略，保留正确的烘焙光照绑定。
主场景始终是唯一编辑源，发布副本不要作为编辑源。

## 检查与导出源码

```powershell
./Tools/Qwen35/Invoke-Qwen.ps1 SourceCheck
./Tools/Qwen35/Invoke-Qwen.ps1 SourcePackage
```

导出的 ZIP 排除本地配置、SDK、模型大文件、编译缓存、报告和构建包。清单保留在 Tools，
本机资源不会删除。检查会扫描候选文件大小、个人路径、疑似密钥与世界绑定，但不替代后续人工审阅。
脚本不会自动创建远程仓库、提交或上传。

更多信息：[实现与维护](Docs/Qwen35-Implementation.md)、[场景规格](Docs/Qwen35-Central-Exhibit-Design.zh-CN.md)、
[天空来源](Docs/Qwen35-Orbital-Sky.md)。
