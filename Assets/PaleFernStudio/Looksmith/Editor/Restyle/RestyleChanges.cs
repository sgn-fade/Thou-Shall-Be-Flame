using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PaleFernStudio.Looksmith.Editor
{
    /// <summary>Restyle changes report which preview group they belong to (one toggle per group in the Hub).</summary>
    public interface IRestyleChange
    {
        string restyleGroup { get; }
    }

    static class RestyleIO
    {
        public static IEnumerable<string> FoldersToCreate(string folder)
        {
            var missing = new List<string>();
            while (!string.IsNullOrEmpty(folder) && folder != "Assets" && !AssetDatabase.IsValidFolder(folder))
            {
                missing.Insert(0, folder);
                folder = Path.GetDirectoryName(folder)?.Replace('\\', '/');
            }
            foreach (var f in missing)
            {
                yield return f;
                yield return f + ".meta";
            }
        }

        public static string Dir(string path) => Path.GetDirectoryName(path)?.Replace('\\', '/');
    }

    /// <summary>A material you own: the shader is swapped in place, so references, prefabs and GUIDs stay as they are.</summary>
    [Serializable]
    public sealed class RestyleMaterialChange : LooksmithChange, IRestyleChange
    {
        public string materialPath;
        public string sourceShader;
        public string groupName = LooksmithRestyle.GroupOwned;
        public RestyleSpec spec;

        public override ChangeGroup group => ChangeGroup.Materials;
        public string restyleGroup => groupName;
        public override string title => $"Restyle {Path.GetFileNameWithoutExtension(materialPath)}  ({sourceShader} → {spec.targetShaderName})";
        public override string detail => materialPath;
        public override IEnumerable<string> Writes() { yield return materialPath; }

        public override void Apply(ChangeContext context)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(materialPath) ?? throw new InvalidOperationException("Material not found: " + materialPath);
            if (!LooksmithMaterialConverter.Convert(m, spec))
                throw new InvalidOperationException($"Target shader {spec.targetShaderName} not found");
            EditorUtility.SetDirty(m);
            AssetDatabase.SaveAssetIfDirty(m);
        }
    }

    /// <summary>
    /// A material inside a model (FBX): Unity's standard "Extract Materials" step (the model's .meta records the
    /// remap; the model file itself is untouched), then the extracted .mat is converted. Extraction removes the
    /// material from the model, so whatever pointed at it directly (prefabs built from the model's pieces, scenes)
    /// is pointed at the extracted .mat in the same step, with those files backed up like the rest.
    /// </summary>
    [Serializable]
    public sealed class ExtractAndRestyleChange : LooksmithChange, IRestyleChange
    {
        public string modelPath;
        public string materialName;
        public string extractedPath;
        public string sourceShader;
        public string groupName = LooksmithRestyle.GroupExtract;
        public RestyleSpec spec;
        public string embeddedReference;                              // "{fileID: …, guid: <model>, type: 3}"
        public List<string> referencingFiles = new List<string>();    // closed prefabs/scenes/assets that use it directly
        public List<string> referencingScenes = new List<string>();   // open scenes with renderers that use it

        public override ChangeGroup group => ChangeGroup.Materials;
        public string restyleGroup => groupName;
        public override string title => $"Extract {materialName} from {Path.GetFileName(modelPath)} and restyle it ({sourceShader} → {spec.targetShaderName})";
        public override string detail => $"Creates {extractedPath}; the model's import settings point at it." +
            (referencingFiles.Count > 0 ? $" {referencingFiles.Count} file(s) that used the model's own material ({string.Join(", ", referencingFiles.Take(3).Select(Path.GetFileName))}{(referencingFiles.Count > 3 ? ", …" : "")}) are pointed at it too." : "");

        public override IEnumerable<string> Writes()
        {
            foreach (var f in RestyleIO.FoldersToCreate(RestyleIO.Dir(extractedPath)))
                yield return f;
            yield return extractedPath;
            yield return modelPath;   // its .meta gains the remap
            foreach (var f in referencingFiles)
                yield return f;
        }

        public override IEnumerable<string> Scenes() => referencingScenes;

        public override void Apply(ChangeContext context)
        {
            var embedded = AssetDatabase.LoadAllAssetsAtPath(modelPath).OfType<Material>().FirstOrDefault(m => m.name == materialName)
                           ?? throw new InvalidOperationException($"{materialName} not found in {modelPath}");
            // renderers in open scenes using the model's own material: extraction leaves those slots empty
            var users = new List<(Renderer renderer, int slot)>();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded)
                    continue;
                foreach (var root in scene.GetRootGameObjects())
                    foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                    {
                        var mats = r.sharedMaterials;
                        for (int s = 0; s < mats.Length; s++)
                            if (mats[s] == embedded)
                                users.Add((r, s));
                    }
            }
            CreateLookProfileChange.EnsureFolder(RestyleIO.Dir(extractedPath));
            var error = AssetDatabase.ExtractAsset(embedded, extractedPath);
            if (!string.IsNullOrEmpty(error))
                throw new InvalidOperationException(error);
            AssetDatabase.WriteImportSettingsIfDirty(modelPath);
            AssetDatabase.ImportAsset(modelPath, ImportAssetOptions.ForceUpdate);
            var m = AssetDatabase.LoadAssetAtPath<Material>(extractedPath) ?? throw new InvalidOperationException("Extraction produced no material at " + extractedPath);
            if (!LooksmithMaterialConverter.Convert(m, spec))
                throw new InvalidOperationException($"Target shader {spec.targetShaderName} not found");
            EditorUtility.SetDirty(m);
            AssetDatabase.SaveAssetIfDirty(m);

            // closed files: their reference to the model's material becomes one to the extracted .mat
            if (referencingFiles.Count > 0 && !string.IsNullOrEmpty(embeddedReference))
            {
                var extracted = $"{{fileID: 2100000, guid: {AssetDatabase.AssetPathToGUID(extractedPath)}, type: 2}}";
                foreach (var f in referencingFiles)
                {
                    var full = LooksmithBackups.FullPath(f);
                    File.WriteAllText(full, File.ReadAllText(full).Replace(embeddedReference, extracted));
                }
                foreach (var f in referencingFiles)
                    AssetDatabase.ImportAsset(f, ImportAssetOptions.ForceUpdate);
            }
            // open scenes: the slots that lost it (prefab instances already follow their re-pointed prefab)
            foreach (var (r, s) in users)
            {
                if (r == null)
                    continue;
                var mats = r.sharedMaterials;
                if (s >= mats.Length || mats[s] == m)
                    continue;
                Undo.RecordObject(r, "Looksmith: use the extracted material");
                mats[s] = m;
                r.sharedMaterials = mats;
                if (PrefabUtility.IsPartOfPrefabInstance(r))
                    PrefabUtility.RecordPrefabInstancePropertyModifications(r);
                context.RecordRemap(r, $"{materialName} → {Path.GetFileNameWithoutExtension(extractedPath)}");
                EditorSceneManager.MarkSceneDirty(r.gameObject.scene);
            }
        }
    }

    /// <summary>A material Looksmith may not edit (in a package or built in): a converted copy in Assets/Looksmith Generated.</summary>
    [Serializable]
    public sealed class GenerateRestyledCopyChange : LooksmithChange, IRestyleChange
    {
        public string sourceGlobalId;   // GlobalObjectId of the source material
        public string sourceName;
        public string sourceShader;
        public string copyPath;
        public string groupName = LooksmithRestyle.GroupCopies;
        public RestyleSpec spec;

        public override ChangeGroup group => ChangeGroup.Materials;
        public string restyleGroup => groupName;
        public override string title => $"Make a restyled copy of {sourceName}  ({sourceShader} → {spec.targetShaderName})";
        public override string detail => $"The original stays untouched; the copy is {copyPath}.";

        public override IEnumerable<string> Writes()
        {
            foreach (var f in RestyleIO.FoldersToCreate(RestyleIO.Dir(copyPath)))
                yield return f;
            yield return copyPath;
        }

        public override void Apply(ChangeContext context)
        {
            if (!GlobalObjectId.TryParse(sourceGlobalId, out var id) || !(GlobalObjectId.GlobalObjectIdentifierToObjectSlow(id) is Material source))
                throw new InvalidOperationException("Source material not found: " + sourceName);
            CreateLookProfileChange.EnsureFolder(RestyleIO.Dir(copyPath));
            var copy = new Material(source) { name = Path.GetFileNameWithoutExtension(copyPath) };
            if (!LooksmithMaterialConverter.Convert(copy, spec))
                throw new InvalidOperationException($"Target shader {spec.targetShaderName} not found");
            AssetDatabase.CreateAsset(copy, copyPath);
            AssetDatabase.SaveAssetIfDirty(copy);
        }
    }

    /// <summary>One renderer slot (or a terrain's material) pointed at a restyled copy.</summary>
    [Serializable]
    public sealed class RemapEntry
    {
        public string objectGlobalId;   // the Renderer or Terrain component
        public int slot;                // material slot (-1 = Terrain.materialTemplate)
        public string fromName;
        public string toPath;
    }

    /// <summary>Points scene renderers at restyled copies. Prefab instances get a normal override; nothing else changes.</summary>
    [Serializable]
    public sealed class RemapRenderersChange : LooksmithChange, IRestyleChange
    {
        public string scenePath;
        public List<RemapEntry> entries = new List<RemapEntry>();
        public string groupName = LooksmithRestyle.GroupCopies;

        public override ChangeGroup group => ChangeGroup.Materials;
        public string restyleGroup => groupName;
        public override string title => $"Use the restyled copies on {entries.Count} renderer slot(s) in {Path.GetFileNameWithoutExtension(scenePath)}";
        public override IEnumerable<string> Writes() { yield return scenePath; }
        public override IEnumerable<string> Scenes() { yield return scenePath; }

        public override void Apply(ChangeContext context)
        {
            var scene = SceneManager.GetSceneByPath(scenePath);
            foreach (var e in entries)
            {
                if (!GlobalObjectId.TryParse(e.objectGlobalId, out var id))
                    continue;
                var target = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(id);
                var material = AssetDatabase.LoadAssetAtPath<Material>(e.toPath);
                if (target == null || material == null)
                    throw new InvalidOperationException($"Remap target missing ({e.fromName} → {e.toPath})");
                Undo.RecordObject(target, "Looksmith: use restyled material");
                if (target is Terrain terrain && e.slot < 0)
                    terrain.materialTemplate = material;
                else if (target is Renderer renderer)
                {
                    // Start from the instance's own materials, so overrides on other slots are kept.
                    var mats = renderer.sharedMaterials;
                    if (e.slot >= mats.Length)
                        continue;
                    mats[e.slot] = material;
                    renderer.sharedMaterials = mats;
                }
                if (PrefabUtility.IsPartOfPrefabInstance(target))
                    PrefabUtility.RecordPrefabInstancePropertyModifications(target);
                context.RecordRemap(target, $"{e.fromName} → {Path.GetFileNameWithoutExtension(e.toPath)}");
            }
            EditorSceneManager.MarkSceneDirty(scene);
        }
    }

    /// <summary>"Map manually…": saves the mapping as a rule asset so the next Restyle knows this shader.</summary>
    [Serializable]
    public sealed class CreateRestyleRuleChange : LooksmithChange, IRestyleChange
    {
        public string rulePath;
        public string sourceShader;
        public RestyleSpec spec;

        public override ChangeGroup group => ChangeGroup.Materials;
        public string restyleGroup => LooksmithRestyle.GroupManual;
        public override string title => $"Save a Restyle rule for {sourceShader} → {spec.targetShaderName}";
        public override string detail => rulePath;

        public override IEnumerable<string> Writes()
        {
            foreach (var f in RestyleIO.FoldersToCreate(RestyleIO.Dir(rulePath)))
                yield return f;
            yield return rulePath;
        }

        public override void Apply(ChangeContext context)
        {
            CreateLookProfileChange.EnsureFolder(RestyleIO.Dir(rulePath));
            var rule = ScriptableObject.CreateInstance<LooksmithRestyleRule>();
            rule.sourceShaders = new[] { sourceShader };
            rule.target = spec.ResolveTarget();
            rule.recipe = spec.recipe;
            rule.vertexColorAmount = spec.vertexColorAmount;
            rule.pairs = spec.pairs.Select(p => new PropertyPair(p.source, p.target)).ToList();
            rule.priority = 100;
            AssetDatabase.CreateAsset(rule, rulePath);
            AssetDatabase.SaveAssetIfDirty(rule);
        }
    }
}
