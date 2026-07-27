        #if UNITY_EDITOR
        using System;
        using System.Collections.Generic;
        using UnityEditor;
        using UnityEditor.Compilation;

        [InitializeOnLoad]
        internal sealed class LakonaGameNuGetPackageImportGuard : AssetPostprocessor
        {
            private const string PreferredRuntimeTfm = "netstandard2.1";
            private const string FallbackRuntimeTfm = "netstandard2.0";

            private static readonly string[] ForbiddenTfms =
            {
                "net10.0", "net9.0", "net8.0", "net7.0", "net6.0",
                "net472", "net48", "net481"
            };

            private static readonly string[] KnownAnalyzerPackageIds =
            {
                "MemoryPack.Generator",
                "Microsoft.CodeAnalysis.Common",
                "Microsoft.CodeAnalysis.CSharp"
            };

            static LakonaGameNuGetPackageImportGuard()
            {
                ApplyNuGetPluginPolicy();
            }

            private static void OnPostprocessAllAssets(
                string[] importedAssets,
                string[] deletedAssets,
                string[] movedAssets,
                string[] movedFromAssetPaths)
            {
                var touched = false;
                foreach (var assetPath in importedAssets)
                {
                    touched |= TryApplyPolicy(assetPath);
                }

                foreach (var assetPath in movedAssets)
                {
                    touched |= TryApplyPolicy(assetPath);
                }

                if (touched)
                {
                    CompilationPipeline.RequestScriptCompilation();
                }
            }

            private static void ApplyNuGetPluginPolicy()
            {
                var changed = false;
                AssetDatabase.StartAssetEditing();
                try
                {
                    var pluginGuids = AssetDatabase.FindAssets("t:PluginImporter", new[] { "Assets/Packages" });
                    foreach (var guid in pluginGuids)
                    {
                        var assetPath = AssetDatabase.GUIDToAssetPath(guid);
                        changed |= TryApplyPolicy(assetPath);
                    }
                }
                finally
                {
                    AssetDatabase.StopAssetEditing();
                }

                if (changed)
                {
                    CompilationPipeline.RequestScriptCompilation();
                }
            }

            private static bool TryApplyPolicy(string assetPath)
            {
                var normalizedPath = assetPath.Replace('\\', '/');
                if (!normalizedPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ||
                    normalizedPath.IndexOf("Assets/Packages/", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    return false;
                }

                var importer = AssetImporter.GetAtPath(assetPath) as PluginImporter;
                if (importer == null)
                {
                    return false;
                }

                if (IsAnalyzerOrGeneratorPlugin(normalizedPath))
                {
                    return DisableAllPlatforms(importer);
                }

                if (TryGetLibAsset(normalizedPath, out var packageRoot, out var tfm, out var fileName))
                {
                    if (IsForbiddenTfm(tfm))
                    {
                        return DisableAllPlatforms(importer);
                    }

                    if (IsPreferredRuntimeTfm(tfm))
                    {
                        return EnableRuntimePlugin(importer);
                    }

                    if (IsFallbackRuntimeTfm(tfm))
                    {
                        return HasHigherPriorityRuntimeSibling(packageRoot, fileName)
                            ? DisableAllPlatforms(importer)
                            : EnableRuntimePlugin(importer);
                    }
                }

                return false;
            }

            private static bool IsAnalyzerOrGeneratorPlugin(string normalizedPath)
            {
                return normalizedPath.IndexOf("/analyzers/", StringComparison.OrdinalIgnoreCase) >= 0
                    || normalizedPath.IndexOf(".Generator.dll", StringComparison.OrdinalIgnoreCase) >= 0
                    || IsKnownAnalyzerPackage(normalizedPath);
            }

            private static bool IsKnownAnalyzerPackage(string normalizedPath)
            {
                foreach (var packageId in KnownAnalyzerPackageIds)
                {
                    var packageMarker = "Assets/Packages/" + packageId + ".";
                    if (normalizedPath.IndexOf(packageMarker, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return true;
                    }
                }

                return false;
            }

            private static bool TryGetLibAsset(
                string normalizedPath,
                out string packageRoot,
                out string tfm,
                out string fileName)
            {
                const string marker = "/lib/";
                var libIndex = normalizedPath.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
                if (libIndex < 0)
                {
                    packageRoot = string.Empty;
                    tfm = string.Empty;
                    fileName = string.Empty;
                    return false;
                }

                var start = libIndex + marker.Length;
                var end = normalizedPath.IndexOf('/', start);
                if (end < 0)
                {
                    packageRoot = string.Empty;
                    tfm = string.Empty;
                    fileName = string.Empty;
                    return false;
                }

                var fileStart = normalizedPath.LastIndexOf('/') + 1;
                if (fileStart <= end)
                {
                    packageRoot = string.Empty;
                    tfm = string.Empty;
                    fileName = string.Empty;
                    return false;
                }

                packageRoot = normalizedPath.Substring(0, libIndex);
                tfm = normalizedPath.Substring(start, end - start);
                fileName = normalizedPath.Substring(fileStart);
                return true;
            }

            private static bool IsPreferredRuntimeTfm(string tfm) =>
                string.Equals(tfm, PreferredRuntimeTfm, StringComparison.OrdinalIgnoreCase);

            private static bool IsFallbackRuntimeTfm(string tfm) =>
                string.Equals(tfm, FallbackRuntimeTfm, StringComparison.OrdinalIgnoreCase);

            private static bool IsForbiddenTfm(string tfm) =>
                Array.Exists(ForbiddenTfms, candidate => string.Equals(candidate, tfm, StringComparison.OrdinalIgnoreCase));

            private static bool HasHigherPriorityRuntimeSibling(string packageRoot, string fileName)
            {
                var preferredPath = packageRoot + "/lib/" + PreferredRuntimeTfm + "/" + fileName;
                return AssetDatabase.LoadMainAssetAtPath(preferredPath) != null;
            }

            private static bool DisableAllPlatforms(PluginImporter importer)
            {
                var changed = false;

                if (importer.GetCompatibleWithAnyPlatform())
                {
                    importer.SetCompatibleWithAnyPlatform(false);
                    changed = true;
                }

                if (importer.GetCompatibleWithEditor())
                {
                    importer.SetCompatibleWithEditor(false);
                    changed = true;
                }

                foreach (var target in EnumerateBuildTargets())
                {
                    if (TryGetCompatibleWithPlatform(importer, target))
                    {
                        changed |= TrySetCompatibleWithPlatform(importer, target, false);
                    }
                }

                if (!changed)
                {
                    return false;
                }

                importer.SaveAndReimport();
                return true;
            }

            private static bool EnableRuntimePlugin(PluginImporter importer)
            {
                if (importer.GetCompatibleWithAnyPlatform())
                {
                    return false;
                }

                importer.SetCompatibleWithAnyPlatform(true);
                importer.SaveAndReimport();
                return true;
            }

            private static IEnumerable<BuildTarget> EnumerateBuildTargets()
            {
                foreach (BuildTarget target in Enum.GetValues(typeof(BuildTarget)))
                {
                    if (target == BuildTarget.NoTarget)
                    {
                        continue;
                    }

                    yield return target;
                }
            }

            private static bool TryGetCompatibleWithPlatform(PluginImporter importer, BuildTarget target)
            {
                try
                {
                    return importer.GetCompatibleWithPlatform(target);
                }
                catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException)
                {
                    return false;
                }
            }

            private static bool TrySetCompatibleWithPlatform(PluginImporter importer, BuildTarget target, bool enabled)
            {
                try
                {
                    importer.SetCompatibleWithPlatform(target, enabled);
                    return true;
                }
                catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException)
                {
                    return false;
                }
            }
        }
        #endif
