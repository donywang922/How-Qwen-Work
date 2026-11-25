using System;
using System.IO;
using System.Linq;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using VRC.SDK3.Components;
using VRC.SDKBase;

namespace QwenWorld.Editor
{
    /// <summary>User-provided furniture and offline reference posters; no remote image loading.</summary>
    public static class QwenSpawnLoungeBuilder
    {
        const string Folder = QwenProject.Root + "/Environment/SpawnLounge";
        const string RootName = "Arrival bench and reference lightbox";
        const int StageLayer = 30;
        const int Width = 864, Height = 1344;
        const int AtlasWidth = 1024, AtlasHeight = 4096, AtlasStep = 1360;
        static Font font;
        static Links source;
        [Serializable] class Link { public int page; public string title,url,qr; }
        [Serializable] class Links { public Link[] links; }
        [Serializable] class QRPlacement { public int page; public float x,y,size; public string url; }
        [Serializable] class QRLayout { public QRPlacement[] codes; }
        [Serializable] class Report
        {
            public bool passed;
            public int furnitureTriangles, pens, seats, pages, links, advertisementPasses, advertisementTextures;
            public Vector3 bench, lightbox;
            public float minimumFurnitureRadius, benchFacesAwayFromCore, lightboxFacesBench;
            public string scope;
        }

        [MenuItem("Qwen World/Environment/Install arrival bench and lightbox")]
        public static void InstallBatch()
        {
            try
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode first.");
                if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
                EditorSceneManager.OpenScene(QwenProject.MainScene,OpenSceneMode.Single);
                PrepareImports();
                RenderPosters();
                QwenModelPipeline.Compile();
                ApplyToCurrentScene();
                var scene=SceneManager.GetActiveScene();
                QwenProject.SaveUdonFields(scene);
                QwenProject.ResetSdkBuildCache(scene);
                EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene); AssetDatabase.SaveAssets();
                ValidateAndPreview();
                Debug.Log("QWEN_SPAWN_LOUNGE_READY");
                if (Application.isBatchMode) EditorApplication.Exit(0);
            }
            catch(Exception error)
            {
                Debug.LogException(error);
                if (Application.isBatchMode) EditorApplication.Exit(1); else throw;
            }
        }

        [MenuItem("Qwen World/Environment/Refresh reference graphics")]
        public static void RefreshReferencesBatch()
        {
            try
            {
                if(EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode first.");
                if(!Application.isBatchMode&&!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
                EditorSceneManager.OpenScene(QwenProject.MainScene,OpenSceneMode.Single);
                PrepareImports(); RenderPosters(); AssetDatabase.SaveAssets();
                var material=AssetDatabase.LoadAssetAtPath<Material>(Folder+"/LightboxPosters.mat");
                if(material==null||material.GetTexture("_MainTex")!=Texture("ReferenceAtlas.png")||material.passCount!=1)
                    throw new InvalidOperationException("Lightbox reference material changed.");
                Debug.Log("QWEN_REFERENCE_GRAPHICS_READY");
                if(Application.isBatchMode) EditorApplication.Exit(0);
            }
            catch(Exception error)
            {
                Debug.LogException(error);
                if(Application.isBatchMode) EditorApplication.Exit(1); else throw;
            }
        }

        static void LoadSource()
        {
            font=AssetDatabase.LoadAssetAtPath<Font>(QwenProject.Root+"/Fonts/NotoSansCJKsc-Regular.otf");
            source=JsonUtility.FromJson<Links>(File.ReadAllText(Folder+"/ReferenceLinks.json"));
        }

        static void PrepareImports()
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            foreach(var path in Directory.GetFiles(Folder,"*.png"))
            {
                if(Path.GetFileName(path)=="ReferenceAtlas.png") continue;
                var importer=(TextureImporter)AssetImporter.GetAtPath(path);
                importer.mipmapEnabled=!Path.GetFileName(path).StartsWith("LinkQR-");
                importer.wrapMode=TextureWrapMode.Clamp; importer.maxTextureSize=2048;
                importer.filterMode=Path.GetFileName(path).StartsWith("LinkQR-")?FilterMode.Point:FilterMode.Bilinear;
                bool height=path.EndsWith("_Bump.png",StringComparison.Ordinal);
                importer.textureType=height?TextureImporterType.NormalMap:TextureImporterType.Default;
                importer.convertToNormalmap=height; importer.heightmapScale=.025f;
                importer.sRGBTexture=!(height||path.Contains("_Metallic")||path.Contains("_Roughness"));
                if(Path.GetFileName(path).StartsWith("LinkQR-"))
                {
                    importer.textureCompression=TextureImporterCompression.Uncompressed;
                    importer.alphaSource=TextureImporterAlphaSource.FromInput; importer.alphaIsTransparency=true;
                    importer.npotScale=TextureImporterNPOTScale.None;
                }
                importer.SaveAndReimport();
            }
            foreach(var path in Directory.GetFiles(Folder,"*.fbx"))
            {
                var importer=(ModelImporter)AssetImporter.GetAtPath(path);
                importer.materialImportMode=ModelImporterMaterialImportMode.None;
                importer.importAnimation=false; importer.importCameras=false; importer.importLights=false;
                importer.SaveAndReimport();
            }
        }

        public static void ApplyToCurrentScene()
        {
            LoadSource();
            var scene=SceneManager.GetActiveScene();
            var old=scene.GetRootGameObjects().FirstOrDefault(o=>o.name==RootName);
            if(old!=null) UnityEngine.Object.DestroyImmediate(old);
            var root=new GameObject(RootName);
            // Visible on the right from Spawn, while every part stays outside the white disc.
            Vector3 benchPoint=new Vector3(11.85f,-.18f,.8f);
            Vector3 outward=new Vector3(benchPoint.x,0,benchPoint.z).normalized;
            Vector3 right=Vector3.Cross(Vector3.up,outward);
            var bench=new GameObject("Bench - back towards compute core"); bench.transform.SetParent(root.transform,false);
            bench.transform.SetPositionAndRotation(benchPoint,Quaternion.LookRotation(outward));
            var benchModel=ImportModel("ParkBench",bench.transform);
            var wood=Material("Bench","Standard"); wood.mainTexture=Texture("T_ParkBench_BaseColor.png");
            wood.SetTexture("_BumpMap",Texture("T_ParkBench_Bump.png")); wood.EnableKeyword("_NORMALMAP");
            wood.SetFloat("_BumpScale",.32f); wood.SetFloat("_Glossiness",.3f);
            SetBenchMetallic(wood);
            benchModel.GetComponentInChildren<MeshRenderer>().sharedMaterial=wood;
            foreach(var mesh in benchModel.GetComponentsInChildren<MeshFilter>()) mesh.gameObject.AddComponent<MeshCollider>().sharedMesh=mesh.sharedMesh;
            for(int i=0;i<2;i++) AddSeat(bench.transform,i);

            var box=new GameObject("Lightbox - left of bench, advertisement towards seats"); box.transform.SetParent(root.transform,false);
            Vector3 point=benchPoint-right*1.85f+outward*.22f;
            box.transform.SetPositionAndRotation(point,Quaternion.LookRotation(benchPoint-point));
            var boxModel=ImportModel("LightboxStand",box.transform);
            var frame=Material("LightboxFrame","Standard"); frame.mainTexture=Texture("T_LightboxStand_ScrollAtlas.png");
            frame.SetFloat("_Metallic",.65f); frame.SetFloat("_Glossiness",.45f);
            var ad=Material("LightboxPosters","QwenWorld/LightboxPages");
            ad.SetTexture("_MainTex",Texture("ReferenceAtlas.png"));
            ad.SetVector("_PageRect",new Vector4(80f/AtlasWidth,16f/AtlasHeight,(float)Width/AtlasWidth,(float)Height/AtlasHeight));
            ad.SetFloat("_PageStride",(float)AtlasStep/AtlasHeight);
            ad.SetFloat("_PageOffset",0);
            RemoveObsoletePageTextures(ad);
            ad.SetFloat("_Duration",24); ad.SetFloat("_Transition",1.1f); ad.SetFloat("_ManualPage",-1);
            boxModel.GetComponentInChildren<MeshRenderer>().sharedMaterials=new[]{frame,ad};
            var display=box.AddUdonSharpComponent<QwenLightbox>(); display.pages=ad;
            ConfigureAdvertisementTargets(display);
            AddPens(box.transform);
            EditorSceneManager.MarkSceneDirty(scene);
        }

        public static void ConfigureAdvertisementTargets(QwenLightbox display)
        {
            var root=display.transform;
            // Do not put an Interact collider on the furniture root: it covers the
            // side rack and causes the whole hierarchy to receive the hover outline.
            foreach(var collider in root.GetComponents<Collider>()) UnityEngine.Object.DestroyImmediate(collider);
            foreach(var child in root.Cast<Transform>().Where(t=>t.name.StartsWith("Advertisement click ",StringComparison.Ordinal)).ToArray())
                UnityEngine.Object.DestroyImmediate(child.gameObject);
            for(int side=0;side<2;side++)
            {
                var go=new GameObject("Advertisement click "+(side==0?"front":"back"),typeof(RectTransform),typeof(Canvas),typeof(GraphicRaycaster));
                var rect=(RectTransform)go.transform; rect.SetParent(root,false);
                rect.sizeDelta=new Vector2(1070,1670); rect.localScale=Vector3.one*.001f;
                rect.localPosition=new Vector3(0,1.165f,side==0?.094f:-.094f);
                rect.localRotation=Quaternion.Euler(0,side==0?180:0,0);
                go.GetComponent<Canvas>().renderMode=RenderMode.WorldSpace;
                var shape=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("VRC.SDK3.Components.VRCUiShape")??a.GetType("VRC.SDKBase.VRC_UiShape")).First(t=>t!=null);
                go.AddComponent(shape); go.AddComponent<BoxCollider>().size=new Vector3(1070,1670,1);
                var image=go.AddComponent<Image>(); image.color=new Color(1,1,1,0); image.raycastTarget=true;
                go.GetComponent<CanvasRenderer>().cullTransparentMesh=false;
                var button=go.AddComponent<Button>(); button.targetGraphic=image; button.transition=Selectable.Transition.None;
                button.navigation=new Navigation{mode=Navigation.Mode.None};
                UnityEventTools.AddStringPersistentListener(button.onClick,UdonSharpEditorUtility.GetBackingUdonBehaviour(display).SendCustomEvent,"NextPage");
            }
        }

        static GameObject ImportModel(string name,Transform parent)
        {
            var go=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Folder+"/"+name+".fbx"));
            go.transform.SetParent(parent,false); return go;
        }

        static void SetBenchMetallic(Material material)
        {
            string path=Folder+"/BenchMetallicSmoothness.png";
            if(!File.Exists(path))
            {
                var metal=ReadData(Texture("T_ParkBench_Metallic.png"));
                var rough=ReadData(Texture("T_ParkBench_Roughness.png"));
                var pixels=metal.GetPixels32(); var r=rough.GetPixels32();
                for(int i=0;i<pixels.Length;i++) pixels[i]=new Color32(pixels[i].r,pixels[i].r,pixels[i].r,(byte)(255-r[i].r));
                var combined=new Texture2D(metal.width,metal.height,TextureFormat.RGBA32,false,true);
                combined.SetPixels32(pixels); combined.Apply(); File.WriteAllBytes(path,combined.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(metal); UnityEngine.Object.DestroyImmediate(rough); UnityEngine.Object.DestroyImmediate(combined);
                AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
                var importer=(TextureImporter)AssetImporter.GetAtPath(path); importer.sRGBTexture=false; importer.SaveAndReimport();
            }
            material.SetTexture("_MetallicGlossMap",Texture("BenchMetallicSmoothness.png")); material.EnableKeyword("_METALLICGLOSSMAP");
        }

        static Texture2D ReadData(Texture texture)
        {
            var previous=RenderTexture.active;
            var target=RenderTexture.GetTemporary(texture.width,texture.height,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear);
            try
            {
                Graphics.Blit(texture,target); RenderTexture.active=target;
                var copy=new Texture2D(texture.width,texture.height,TextureFormat.RGBA32,false,true);
                copy.ReadPixels(new Rect(0,0,texture.width,texture.height),0,0); copy.Apply(); return copy;
            }
            finally { RenderTexture.active=previous; RenderTexture.ReleaseTemporary(target); }
        }

        static void AddSeat(Transform parent,int index)
        {
            var seat=new GameObject("Seat "+(index+1)); seat.transform.SetParent(parent,false);
            seat.transform.localPosition=new Vector3(index==0?-.37f:.37f,.43f,.02f);
            var station=seat.AddComponent<VRC.SDK3.Components.VRCStation>(); station.seated=true; station.disableStationExit=false;
            var enter=new GameObject("Sit pose").transform; enter.SetParent(seat.transform,false);
            var exit=new GameObject("Exit towards outer deck").transform; exit.SetParent(seat.transform,false); exit.localPosition=new Vector3(0,-.43f,.75f);
            station.stationEnterPlayerLocation=enter; station.stationExitPlayerLocation=exit;
            var hit=seat.AddComponent<BoxCollider>(); hit.size=new Vector3(.63f,.12f,.40f);
            var script=seat.AddUdonSharpComponent<QwenBenchSeat>(); script.station=station;
            var udon=UdonSharpEditorUtility.GetBackingUdonBehaviour(script); udon.interactText="坐下 / Sit"; udon.proximity=2;
        }

        static void AddPens(Transform box)
        {
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Packages/net.ureishi.qvpen/QvPen.prefab");
            if(prefab==null) throw new InvalidOperationException("Restore the QvPen VPM package before installing furniture.");
            var set=(GameObject)PrefabUtility.InstantiatePrefab(prefab); set.name="QvPen - 45 degree side rack"; set.transform.SetParent(box,false);
            set.transform.localPosition=Vector3.zero;
            // Keep the upstream settings, network objects and all pen identities intact.
            var managers=set.GetComponentsInChildren<QvPen.UdonScript.QvPen_PenManager>(true);
            for(int i=0;i<managers.Length;i++)
            {
                var manager=managers[i].transform;
                var pickup=manager.GetComponentInChildren<VRC_Pickup>(true);
                pickup.transform.localScale*=.60f;
                var body=pickup.GetComponentsInChildren<MeshFilter>(true).OrderByDescending(m=>m.sharedMesh.vertexCount).First();
                Vector3 ext=body.sharedMesh.bounds.size;
                Vector3 axis=ext.x>ext.y&&ext.x>ext.z?Vector3.right:(ext.y>ext.z?Vector3.up:Vector3.forward);
                Vector3 current=body.transform.TransformDirection(axis).normalized;
                Vector3 desired=box.TransformDirection(new Vector3(0,1,1).normalized);
                manager.rotation=Quaternion.FromToRotation(current,desired)*manager.rotation;
                Vector3 center=body.transform.TransformPoint(body.sharedMesh.bounds.center);
                Vector3 destination=box.TransformPoint(new Vector3(.658f,.40f+i*1.32f/Mathf.Max(1,managers.Length-1),0));
                manager.position+=destination-center;
            }
            foreach(var canvas in set.GetComponentsInChildren<Canvas>(true)) canvas.gameObject.SetActive(false);
            // The eraser is accessible below the rack; its original networking stays intact.
            var erasers=set.transform.Find("Erasers");
            if(erasers!=null) { erasers.localPosition=new Vector3(.66f,.25f,0); erasers.localRotation=Quaternion.identity; }
            foreach(var pickup in set.GetComponentsInChildren<VRC_Pickup>(true))
                if(pickup.GetComponentInParent<QvPen.UdonScript.QvPen_PenManager>()==null)
                    pickup.transform.position=box.TransformPoint(new Vector3(.66f,.24f,0));
        }

        static void RemoveObsoletePageTextures(Material material)
        {
            var serialized=new SerializedObject(material);
            var textures=serialized.FindProperty("m_SavedProperties.m_TexEnvs");
            for(int i=textures.arraySize-1;i>=0;i--)
            {
                string key=textures.GetArrayElementAtIndex(i).FindPropertyRelative("first").stringValue;
                if(key=="_Page0"||key=="_Page1"||key=="_Page2") textures.DeleteArrayElementAtIndex(i);
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        static void RenderPosters()
        {
            LoadSource();
            var stage=new GameObject("Temporary reference stage");
            var cameraObject=new GameObject("Temporary reference camera");
            var previousFog=RenderSettings.fog; RenderSettings.fog=false;
            string output=QwenProject.DataPath("art/spawn-lounge"); Directory.CreateDirectory(output);
            var atlas=new Texture2D(AtlasWidth,AtlasHeight,TextureFormat.RGB24,false,false);
            var codeLayout=new System.Collections.Generic.List<QRPlacement>();
            atlas.SetPixels32(Enumerable.Repeat(new Color32(8,10,18,255),AtlasWidth*AtlasHeight).ToArray());
            try
            {
                var camera=cameraObject.AddComponent<Camera>(); camera.enabled=false; camera.cullingMask=1<<StageLayer;
                camera.nearClipPlane=.01f; camera.farClipPlane=100; camera.allowHDR=false;
                camera.transform.position=Vector3.zero; camera.aspect=(float)Width/Height;
                for(int page=0;page<3;page++)
                {
                    foreach(var child in stage.transform.Cast<Transform>().ToArray()) UnityEngine.Object.DestroyImmediate(child.gameObject);
                    camera.clearFlags=page==0?CameraClearFlags.Skybox:CameraClearFlags.SolidColor;
                    camera.backgroundColor=page==1?new Color(.025f,.022f,.075f):new Color(.035f,.055f,.07f);
                    camera.orthographic=page==2; camera.orthographicSize=1.2f; camera.fieldOfView=72;
                    camera.transform.rotation=page==0?Quaternion.Euler(-27.5f,66,0):Quaternion.identity;
                    var canvasObject=new GameObject("Reference layout",typeof(RectTransform),typeof(Canvas));
                    canvasObject.transform.SetParent(stage.transform,false); canvasObject.layer=StageLayer;
                    var canvas=canvasObject.GetComponent<Canvas>(); canvas.renderMode=RenderMode.ScreenSpaceCamera; canvas.worldCamera=camera; canvas.planeDistance=.05f;
                    var layout=(RectTransform)canvasObject.transform;
                    Texture2D original=null;
                    if(page==1)
                    {
                        original=new Texture2D(2,2,TextureFormat.RGBA32,false,false);
                        original.LoadImage(File.ReadAllBytes(QwenProject.Root+"/Environment/QwenGirl/QwenGirl-Open.png"));
                        original.wrapMode=TextureWrapMode.Clamp;
                        var girl=Raw(layout,"Head to thigh - aspect preserved",0,0,Width,Height,original);
                        float cropHeight=.66f;
                        float cropWidth=((float)Width/Height)*cropHeight*original.height/original.width;
                        // Sample beyond the transparent left image edge to move the portrait
                        // right without clipping its hair on an internal rectangular boundary.
                        girl.uvRect=new Rect(.70f-cropWidth,1-cropHeight,cropWidth,cropHeight);
                    }
                    if(page==2)
                    {
                        var photo=new Texture2D(2,2,TextureFormat.RGB24,false,false);
                        photo.LoadImage(File.ReadAllBytes(Folder+"/PencilBackground.png"));
                        original=photo;
                        var backdrop=Raw(layout,"Pencils with empty citation area",0,0,Width,Height,photo);
                        float cropWidth=((float)Width/Height)*photo.height/photo.width;
                        backdrop.uvRect=new Rect((1-cropWidth)*.5f,0,cropWidth,1);
                    }
                    var links=source.links.Where(l=>l.page==page).ToArray();
                    float y=page==1?68:55;
                    for(int i=0;i<links.Length;i++)
                    {
                        const float qr=80;
                        Raw(layout,"QR "+i,38,y+2,qr,qr,Texture(links[i].qr));
                        codeLayout.Add(new QRPlacement{page=page,x=38,y=y+2,size=qr,url=links[i].url});
                        float x=136, w=page==1?210:Width-x-42;
                        string citation=Citation(page,i);
                        var label=Text(layout,"Citation "+i,x,y,w,100,18,"["+(i+1)+"] "+citation,new Color(.93f,.93f,.91f));
                        label.lineSpacing=.80f;
                        float titleHeight=Mathf.Ceil(label.preferredHeight);
                        label.rectTransform.sizeDelta=new Vector2(w,titleHeight+2);
                        string address=links[i].url.Replace("https://","");
                        if(page==1) address=new[]{"huggingface.co/Qwen/\nQwen3.5-0.8B","gitlab.com/lox9973/\nShaderGPT","pixiv.net/artworks/\n148186519"}[i];
                        var url=Text(layout,"URL "+i,x,y+titleHeight+2,w,100,16,address,new Color(.76f,.81f,.86f));
                        url.lineSpacing=.82f;
                        float urlHeight=Mathf.Ceil(url.preferredHeight);
                        url.rectTransform.sizeDelta=new Vector2(w,urlHeight+2);
                        y+=Mathf.Max(qr,titleHeight+2+urlHeight)+16;
                    }
                    string[] notes={"背景由上述资料合成。", "AI生成；已抠图及修改表情。\n立绘需署名，不可商用。", ""};
                    if(notes[page].Length>0)
                    {
                        var note=Text(layout,"Source conditions",38,y+5,page==1?200:Width-80,80,14,notes[page],new Color(.73f,.77f,.81f));
                        note.lineSpacing=.85f;
                    }
                    var labels=layout.GetComponentsInChildren<Text>();
                    foreach(var label in labels) font.RequestCharactersInTexture(label.text,label.fontSize,FontStyle.Normal);
                    foreach(var label in labels) label.SetAllDirty(); Canvas.ForceUpdateCanvases();
                    foreach(var label in labels) label.SetAllDirty(); Canvas.ForceUpdateCanvases();
                    var pixels=QwenPreview.Capture(camera,output+"/page-"+page+".png",Width,Height);
                    atlas.SetPixels32(80,16+page*AtlasStep,Width,Height,pixels);
                    if(original!=null) UnityEngine.Object.DestroyImmediate(original);
                }
                atlas.Apply(); File.WriteAllBytes(Folder+"/ReferenceAtlas.png",atlas.EncodeToPNG());
                File.WriteAllText(output+"/layout.json",JsonUtility.ToJson(new QRLayout{codes=codeLayout.ToArray()},true));
            }
            finally
            {
                RenderSettings.fog=previousFog;
                UnityEngine.Object.DestroyImmediate(atlas); UnityEngine.Object.DestroyImmediate(stage); UnityEngine.Object.DestroyImmediate(cameraObject);
            }
            AssetDatabase.ImportAsset(Folder+"/ReferenceAtlas.png",ImportAssetOptions.ForceSynchronousImport);
            var importer=(TextureImporter)AssetImporter.GetAtPath(Folder+"/ReferenceAtlas.png");
            importer.maxTextureSize=4096; importer.npotScale=TextureImporterNPOTScale.None; importer.wrapMode=TextureWrapMode.Clamp;
            importer.mipmapEnabled=true; importer.textureCompression=TextureImporterCompression.Uncompressed; importer.alphaSource=TextureImporterAlphaSource.None;
            importer.SaveAndReimport();
        }

        static string Citation(int page,int index)
        {
            string[][] entries={
                new[]{"Wright, E.; NASA/GSFC; ESA/Gaia/DPAC. Deep Star Maps 2020.","Stoeckli, R.; NASA Earth Observatory. Blue Marble Next Generation.","NASA Earth Observatory; Suomi NPP/VIIRS. Black Marble (2016)."},
                new[]{"Qwen. Qwen3.5-0.8B.","lox9973. ShaderGPT.","ZipZipPipe. AI娘化 (2026)."},
                new[]{"ureishi. QvPen.","VRChat. Worlds SDK.","VRChat. UdonSharp.","VRChat. Creator Companion."}
            };
            return entries[page][index];
        }

        static RectTransform Rect(Transform parent,string name,float x,float y,float w,float h)
        {
            var rect=new GameObject(name,typeof(RectTransform)).GetComponent<RectTransform>(); rect.gameObject.layer=parent.gameObject.layer;
            rect.SetParent(parent,false); rect.anchorMin=rect.anchorMax=new Vector2(0,1); rect.pivot=new Vector2(0,1);
            rect.anchoredPosition=new Vector2(x,-y); rect.sizeDelta=new Vector2(w,h); return rect;
        }
        static RawImage Raw(Transform p,string n,float x,float y,float w,float h,Texture t)
        { var im=Rect(p,n,x,y,w,h).gameObject.AddComponent<RawImage>(); im.texture=t; im.raycastTarget=false; return im; }
        static Text Text(Transform p,string n,float x,float y,float w,float h,int size,string value,Color c)
        {
            var t=Rect(p,n,x,y,w,h).gameObject.AddComponent<Text>(); t.font=font; t.fontSize=size; t.color=c; t.text=value;
            t.supportRichText=false; t.horizontalOverflow=HorizontalWrapMode.Wrap; t.verticalOverflow=VerticalWrapMode.Truncate; t.raycastTarget=false;
            return t;
        }
        static Texture2D Texture(string name) => AssetDatabase.LoadAssetAtPath<Texture2D>(Folder+"/"+name);
        static Material Material(string name,string shader)
        {
            string path=Folder+"/"+name+".mat";
            var mat=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(mat==null) {mat=new Material(Shader.Find(shader));AssetDatabase.CreateAsset(mat,path);} else mat.shader=Shader.Find(shader);
            EditorUtility.SetDirty(mat); return mat;
        }

        static void ValidateAndPreview()
        {
            var root=GameObject.Find(RootName); var bench=root.transform.GetChild(0); var lightbox=root.transform.GetChild(1);
            int triangles=0; float radius=float.MaxValue;
            foreach(var model in new[]{bench,lightbox})
                foreach(var filter in model.GetChild(0).GetComponentsInChildren<MeshFilter>())
                {
                    triangles+=filter.sharedMesh.triangles.Length/3;
                    foreach(var vertex in filter.sharedMesh.vertices)
                    {
                        var v=filter.transform.TransformPoint(vertex); radius=Mathf.Min(radius,new Vector2(v.x,v.z).magnitude);
                    }
                }
            var report=new Report{passed=true,furnitureTriangles=triangles,pens=root.GetComponentsInChildren<QvPen.UdonScript.QvPen_PenManager>(true).Length,
                seats=root.GetComponentsInChildren<QwenBenchSeat>().Length,pages=3,links=source.links.Length,bench=bench.position,lightbox=lightbox.position,
                minimumFurnitureRadius=radius,benchFacesAwayFromCore=Vector3.Dot(bench.forward,new Vector3(bench.position.x,0,bench.position.z).normalized),
                lightboxFacesBench=Vector3.Dot(lightbox.forward,(bench.position-lightbox.position).normalized),
                scope="Furniture placement and materials, two seat bindings, QvPen pickup references, one reference atlas, no reference Canvas, QR round-trip (Python), Udon compilation and editor previews. No world build, network or native-client test."};
            if(radius<11.2f||triangles!=2826||report.pens<1||report.seats!=2||report.lightboxFacesBench<.99f) throw new InvalidOperationException("Arrival furniture geometry check failed: "+JsonUtility.ToJson(report));
            if(ShaderUtil.ShaderHasError(AssetDatabase.LoadAssetAtPath<Material>(Folder+"/LightboxPosters.mat").shader)) throw new InvalidOperationException("Lightbox shader compile failed.");
            string output=QwenProject.DataPath("validation/spawn-lounge"); Directory.CreateDirectory(output);
            QwenPreview.Capture(output+"/spawn-right.png",new Vector3(0,1.5f,-11.8f),new Vector3(0,1.5f,0),76,1600,1000);
            QwenPreview.Capture(output+"/furniture.png",bench.position+bench.forward*3+Vector3.up*1.45f-bench.right*2.4f,bench.position-bench.right*.65f+Vector3.up*.8f,58,1600,1000);
            var ad=AssetDatabase.LoadAssetAtPath<Material>(Folder+"/LightboxPosters.mat");
            report.advertisementPasses=ad.passCount;
            report.advertisementTextures=ad.GetTexturePropertyNames().Length;
            if(report.advertisementPasses!=1||report.advertisementTextures!=1) throw new InvalidOperationException("Expected a single-pass, single-texture reference material.");
            ad.SetFloat("_ManualPage",0);
            QwenPreview.Capture(output+"/lightbox.png",lightbox.position+lightbox.forward*2.8f+Vector3.up*1.2f,lightbox.position+Vector3.up*1.1f,48,1000,1400);
            if(lightbox.GetComponentsInChildren<Canvas>(true).Any(c=>!c.transform.IsChildOf(lightbox.Find("QvPen - 45 degree side rack"))&&!c.name.StartsWith("Advertisement click ",StringComparison.Ordinal)))
                throw new InvalidOperationException("Obsolete reference UI remains.");
            var atlas=ad.GetTexture("_MainTex");
            if(atlas==null||atlas.width!=AtlasWidth||atlas.height!=AtlasHeight) throw new InvalidOperationException("Reference atlas size differs.");
            for(int page=0;page<3;page++)
                AssetDatabase.DeleteAsset(Folder+"/Poster-"+page+".png");
            ad.SetFloat("_ManualPage",-1); EditorUtility.SetDirty(ad); AssetDatabase.SaveAssets();
            File.WriteAllText(output+"/report.json",JsonUtility.ToJson(report,true)+"\n");
        }
    }
}
