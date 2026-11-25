# 出生点长椅与资料灯箱

主场景根节点：`Arrival bench and reference lightbox`。家具在出生点视野右侧的透明地面上，位于白色地板边缘外。
长椅背朝核心，提供两个座位；灯箱在坐姿左侧，正面朝向长椅。侧面挂有倾斜 45° 的 QvPen。

## 内容

三页只使用编号引用、作者、资源名、原始网址和二维码，不含广告标题或标语。
二维码仅显示浅色模块、透明间隙，无白色底板或边框，大小为每页 80 像素。字体使用紧凑行距。
NASA 和铅笔页不添加黑底，直接在背景的暗部排版；千问页把立绘移至右侧，引用和码排在左侧暗部。

1. NASA 星图、Blue Marble、Black Marble。背景使用场景同源的真实星空/地球合成全景，地球占下方约三分之一。
2. Qwen3.5-0.8B、ShaderGPT、ZipZipPipe《AI娘化》。背景立绘保留头部、裁到大腿；按原始 PNG 宽高比计算 UV，保持人物比例。
3. QvPen、VRChat Worlds SDK、UdonSharp、Creator Companion。背景通过内置 image_gen 生图：彩色铅笔排在下方，上方预留深色空白放引用与二维码；原始提示词和图片校验值见 `SpawnLounge-Pencil-Source.json`。

立绘及其在图集中的派生展示保留“需署名、不可商用”条件，见 `QwenGirl-Usage-Terms.md`。
NASA/ESA 来源署名保留在第一页面和来源文档中。

## 显示与操作

灯箱没有展开面板、输入框或页码按钮。原始网址和二维码直接印在引用页上。
每页 24 秒，最后 1.1 秒向上滚动切换；点击灯箱直接跳到下一页，随后继续自动轮播。
换页是本地操作，不更改模型、聊天或 QvPen 原有同步。
点击范围仅为正反广告面（各 1.07×1.67 米），通过完全透明、无视觉过渡的 UI 按钮接收点击。
灯箱根节点不再挂整块交互碰撞盒或 Udon Interact 事件，避免覆盖笔架抓取及触发整物体高亮。

运行时只引用一张 `ReferenceAtlas.png`：1024×4096，包含三块 864×1344 的页面区域和边距。
广告子网格使用一个材质、一个 Pass；每个片元仅采样该图集一次，换页由 UV 决定。
三个页面不会同时作为三个 UI 面板绘制。灯箱外框仍有独立的 PBR 材质。
单页预览生成到配置工作目录的 `art/spawn-lounge/page-*.png`，不导入场景。

## 维护

- Blender 原件：`Tools/Blender/SpawnLounge/LightboxStand.blend`、`ParkBench.blend`，贴图已内嵌。
- 导出脚本：同目录 `export_assets.py`；排除摄影棚、相机和灯光，仅导出家具模型。
- Unity 资源：`Assets/QwenWorld/Environment/SpawnLounge`。
- 引用与二维码：`Tools/Qwen35/make_lounge_links.py`、`ReferenceLinks.json`。
- 场景/图集生成器：`QwenSpawnLoungeBuilder`。
- 完整安装菜单：`Qwen World > Environment > Install arrival bench and lightbox`。
- 仅修改引用图片：`Qwen World > Environment > Refresh reference graphics`，不重建家具或主动编译 Udon。
- 主场景重建器会恢复家具。不会生成或更新源码 ZIP。

画笔依赖 [QvPen / ureishi](https://github.com/ureishi/QvPen) 3.3.15，使用原版组件与网络引用。
长椅使用 VRChat 标准站点入座和离座，出口在长椅前方的透明地面上。

安装检查范围为模型/引用资源、朝向、地板边界、材质编译和局部编辑器预览。
不运行模型推理、双平台构建、源码打包或自动发布。
