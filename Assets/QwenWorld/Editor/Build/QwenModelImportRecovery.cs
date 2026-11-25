using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace QwenWorld.Editor
{
    /// <summary>Restore generated model import recipes without touching payloads or scenes.</summary>
    public static class QwenModelImportRecovery
    {
        [Serializable] class Manifest { public string modelFolder; public Payload[] payloads; }
        [Serializable] class Payload { public string path,meta; }
        [Serializable] class Report
        {
            public bool passed;
            public int checkedTextures,restoredSettings,forcedReimports,largestWidth,largestHeight,maxTextureSize;
            public string largestFormat,scope;
        }
        [MenuItem("Qwen World/Model/Restore original texture import settings")]
        public static void RestoreBatch()
        {
            try
            {
                if(EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode first.");
                var manifest=JsonUtility.FromJson<Manifest>(File.ReadAllText("Tools/Qwen35/model-payloads.lock.json"));
                var textures=manifest.payloads.Where(p=>p.path.EndsWith(".png",StringComparison.Ordinal)||p.path.EndsWith(".exr",StringComparison.Ordinal)).ToArray();
                int changed=0;
                foreach(var payload in textures)
                {
                    string path=manifest.modelFolder+"/"+payload.path+".meta";
                    string current=File.ReadAllText(path).Replace("\r\n","\n");
                    if(current==payload.meta) continue;
                    string before=Regex.Match(current,@"(?m)^guid: ([0-9a-f]+)").Groups[1].Value;
                    string expected=Regex.Match(payload.meta,@"(?m)^guid: ([0-9a-f]+)").Groups[1].Value;
                    if(before!=expected) throw new InvalidOperationException("Model GUID differs; restore original payload GUIDs first: "+payload.path);
                    string backup=QwenProject.DataPath("archive/model-import-recovery/"+payload.path+".meta");
                    Directory.CreateDirectory(Path.GetDirectoryName(backup));
                    if(!File.Exists(backup)) File.WriteAllText(backup,current,new UTF8Encoding(false));
                    File.WriteAllText(path,payload.meta,new UTF8Encoding(false)); changed++;
                }
                if(changed>0) AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                const string embedding="model.embed_tokens/model.embed_tokens.weight.T.png";
                string largePath=manifest.modelFolder+"/"+embedding;
                var importer=(TextureImporter)AssetImporter.GetAtPath(largePath);
                var large=AssetDatabase.LoadAssetAtPath<Texture2D>(largePath);
                int reimports=0;
                if(large==null||large.width!=15520||large.height!=4096)
                {
                    AssetDatabase.ImportAsset(largePath,ImportAssetOptions.ForceUpdate|ImportAssetOptions.ForceSynchronousImport);
                    large=AssetDatabase.LoadAssetAtPath<Texture2D>(largePath); reimports++;
                }
                if(large==null||large.width!=15520||large.height!=4096||importer.maxTextureSize!=16384)
                    throw new InvalidOperationException("Embedding texture is not loaded at its original dimensions.");
                var report=new Report{passed=true,checkedTextures=textures.Length,restoredSettings=changed,forcedReimports=reimports,
                    largestWidth=large.width,largestHeight=large.height,maxTextureSize=importer.maxTextureSize,largestFormat=large.format.ToString(),
                    scope="All model texture import recipes compared/restored with original GUIDs; actual oversized embedding dimensions checked. No quantization, inference, scene changes, build or source archive."};
                string output=QwenProject.DataPath("validation/model-import-recovery.json"); Directory.CreateDirectory(Path.GetDirectoryName(output));
                File.WriteAllText(output,JsonUtility.ToJson(report,true)+"\n");
                Debug.Log("QWEN_MODEL_IMPORTS_RECOVERED "+JsonUtility.ToJson(report));
                if(Application.isBatchMode) EditorApplication.Exit(0);
            }
            catch(Exception error)
            {
                Debug.LogException(error);
                if(Application.isBatchMode) EditorApplication.Exit(1); else throw;
            }
        }
    }
}
