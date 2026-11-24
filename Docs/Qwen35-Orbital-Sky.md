# 轨道天空与机械外环

主场景使用用户建模工程交付的 `Orbital_Ring_20K_Distant`，Android 从同一主场景生成。源 Blender 工程未修改；项目保留 FBX、五张源贴图与交付报告供离线重建。

## 模型与摆放

- 19,954 个三角形、一个网格、一个子网格、一个 Renderer、一个材质；UV 接缝及法线拆分后 Unity 中有 39,774 个顶点。
- 保留源模型中的全部几何及五张贴图。细管和薄件包含双面片，材质必须保持双面显示。
- 环心为 (0,0,0)，与白色地面顶面的圆心重合。环面正对地球，法线朝向天空地球中心（yaw 66°、elevation -35°），由 `Quaternion.FromToRotation(Vector3.up, EarthDirection)` 计算。测量源模型实际最外沿半径，统一缩放到外沿半径 22.4 米，与透明地面的 44.8 米直径匹配，不单独压缩截面。
- 导入器检测到 FBX 的薄轴为 Z，先以纯旋转将环面转到 Unity XZ，再烘入朝向地球的姿态。倾斜环的俯视投影为椭圆，两种不同平面的轮廓不会处处重合。没有通往中心的拉线。
- 原来的单独灯条网格和旧顶点色材质已从场景及天空资源包中解除引用。蓝灯由新模型的 Emission 贴图和同一个材质显示。

远景版主要压缩截面、厚度及圆周分段，近看会看到交叉片和棱角。这是源模型的远景设计，导入时没有进一步减面或删除零件。

## 工程资源

- `Assets/QwenWorld/Environment/ImportedOrbitalRing/Orbital_Ring_20K_Distant.fbx`：从已保存 Blender 工程导出的源网格，只导出 Mesh，没有相机、灯光和动画。
- `ImportedOrbitalRing/textures/`：BaseColor、Normal、Metallic、Roughness、Emission 五张完整 4096×4096 原图；逐文件 SHA-256 与源交付一致。
- `ImportedOrbitalRing/source-report.json`：建模线程提供的源资产报告。
- `Environment/OrbitalRing.asset`：烘入世界坐标的 Unity 网格，供场景直接引用。
- `Environment/OrbitalStructure.mat`：单个双面 PBR 材质。
- `Shaders/Environment/OrbitalRingPBR.shader`：不透明、Cull Off、法线/金属度/粗糙度/自发光贴图，基于 Unity Standard BRDF 的固定方向光照。法线强度 0.18、自发光强度 3，与源材质参数一致。没有新增实时 Light、相机、CRT 或 Udon 更新。
- `Editor/Environment/QwenOrbitalRingMesh.cs`：FBX 与贴图导入配置、轴向校正、世界网格烘焙与材质绑定。没有旧环生成的后备路径。

为远景控制纹理载荷，完整源图保留在工程中，平台导入尺寸如下。所有贴图开启 mipmap、关闭 CPU Read/Write。

| 贴图 | PC | Android |
|---|---|---|
| BaseColor | 4096，BC7，sRGB | 2048，ASTC 6×6，sRGB |
| Normal | 2048，BC5，NormalMap | 1024，ASTC 6×6，NormalMap |
| Metallic / Roughness | 各 2048，BC4，线性 | 各 1024，ASTC 6×6，线性 |
| Emission | 1024，BC7，sRGB | 1024，ASTC 6×6，sRGB |

天空使用 `OrbitalSky.png` 的 4096×2048 NASA 天空合成图及 `Skybox/Panoramic` 材质。PC 导入 BC7，Android 导入为 2048×1024 ASTC 6×6；当前内容为下述日夜混合版本。

### 日夜混合与增强边缘辉光

按用户新的明暗要求，将白天地表和夜面城市灯光叠加。令投影圆盘的归一化半径为 r，中心为 0、轮廓为 1，白天图占比采用 `0.22 + 0.68 * r^1.65`，从中心 22% 平滑增加至轮廓 90%。中心保留可辨认的海洋、陆地和城市灯光；夜面添加微弱暗蓝底色，避免海洋被压成纯黑。城市灯光向边缘适度减弱，保留地表细节。

保持地球中心方向（yaw 66°、elevation -35°）、49° 视半径、星空位置与曝光不变；太阳仍位于地球左上边缘后方，距地球中心 48.65°。太阳圆盘保持完全遮挡，白天地表层采用用户指定的艺术混合比例。入射值统计只描述太阳遮挡几何，画面日夜比例由上述混合曲线决定。

大气核心宽度由 0.0017 增至 0.0055 弧度，外侧光晕衰减尺度由 0.005 增至 0.018 弧度，并加入 0.045 弧度的较弱外晕。核心及光晕整体提亮，背离太阳方向也保留可见的蓝色辉光；掩日附近有范围更宽的暖白色微光。太阳圆盘本身没有绘制。

`Tools/Qwen35/make_orbital_sky.py` 为可重复烘焙入口，源 NASA EXR/JPEG 及校验清单位于 `<work>/art/sky/nasa`，烘焙参数、遮挡余量和入射值统计写入 `<work>/art/sky/bake-manifest.json`。这是根据真实源图制作的艺术合成，不是特定轨道、时间和相机曝光的科学复现。


## 当前维护入口

`Editor/Environment/QwenOrbitalRingMesh.cs` 负责 FBX 与纹理导入和世界网格烘焙，`QwenSkyBuilder` 负责天空与外环安装。重装主展厅由 `QwenSceneInstaller.InstallBatch` 完成，平台缓存随后从主场景生成。无需打开 Blender。

白化仅作用于装甲对应的图集区域 0、1、10、14，保留深灰框架、金属管道、PBR 五张图和蓝色灯条。不要用整体纯白材质替换，否则会失去细节。

天空生成入口是 `Tools/Qwen35/make_orbital_sky.py`，源素材与 SHA256 清单位于外部 `QWEN_DATA_ROOT/art/sky/nasa`，只有最终合成全景导入 Unity。NASA、Gaia 与原作者署名保存在 `Assets/QwenWorld/Environment/SkySources.txt`。这些素材用于艺术合成，不代表 NASA 背书。

当前世界包、平台剔除与渲染验证以 `Qwen35-Validation-Summary.json` 为准。早期外环试模和天空实验只保存在外部归档及历史验证目录，不再保留对应的临时编辑器验证类。
