using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using TMPro;
using System.Text;
using System;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.ResourceManagement.AsyncOperations;
using System.Collections;
using Unity.EditorCoroutines.Editor;
using UnityEditor.Localization.Plugins.Google;
using UnityEditor.Localization;
using System.Linq;

namespace ManaBingsu.OneButtonLocalizedTMProFontAtlasGenerator
{
    [CreateAssetMenu(fileName = "One button localized TMPro font atlas generator tool", menuName = "ManaBingsu/OneButtonLocalizedTMProFontAtlasGeneratorTool", order = 0)]
    public class OneButtonLocalizedTMProFontAtlasGeneratorTool : ScriptableObject
    {
        [Header("Option")]
        [SerializeField]
        private bool syncTableWithGoogleSheet;

        [Header("Essential references")]
        [SerializeField]
        private List<StringTableCollection> stringTableInfos;

        [SerializeField]
        private List<LocaleFontAssets> localeFontAssets = new List<LocaleFontAssets>();

        [Header("Google sheets")]
        [SerializeField]
        private SheetsServiceProvider sheetsServiceProvider;

        [Header("Status")]
        [SerializeField]
        private bool isLoad = false;

        public void GenerateFontAtlas()
        {
            if (isLoad)
                return;
            isLoad = true;
            EditorCoroutineUtility.StartCoroutine(RunGenerateFontAtlas(), this);
        }

        private IEnumerator RunGenerateFontAtlas()
        {
            if (syncTableWithGoogleSheet)
                SyncTablesWithGoogleSheets();

            foreach (var localeFontAsset in localeFontAssets)
            {
                var locationKey = localeFontAsset.Locale;
                StringBuilder strBuilder = new StringBuilder();

                foreach (var stringTableInfo in stringTableInfos)
                {
                    var stringOperation = LocalizationSettings.StringDatabase.GetTableAsync(stringTableInfo.name, locationKey);
                    Debug.Log(stringTableInfo.name);
                    while (!(stringOperation.IsDone && stringOperation.Status == AsyncOperationStatus.Succeeded))
                    {
                        yield return null;
                    }

                    var table = stringOperation.Result.Values;

                    foreach (var item in table)
                    {
                        strBuilder.Append(item.Value);
                        Debug.Log($"[{ stringTableInfo.name }] {item.Value}");
                    }

                    yield return null;
                }

                foreach (TMP_FontAsset fontAsset in localeFontAsset.FontAssets)
                {
                    if (fontAsset == null)
                    {
                        Debug.LogError("<color=blue>[Error] </color> Font asset doesn't set in list");
                        continue;
                    }

                    Debug.Log("Bake [" + localeFontAsset.Name + "](" + fontAsset.name + ")");

                    // Delete duplicates
                    string characterSequence = strBuilder.ToString();
                    uint[] characterSet = null;
                    List<uint> characters = new List<uint>();
                    for (int i = 0; i < characterSequence.Length; i++)
                    {
                        uint unicode = characterSequence[i];
                        // Handle surrogate pairs
                        if (i < characterSequence.Length - 1 && char.IsHighSurrogate((char)unicode) && char.IsLowSurrogate(characterSequence[i + 1]))
                        {
                            unicode = (uint)char.ConvertToUtf32(characterSequence[i], characterSequence[i + 1]);
                            i += 1;
                        }
                        // Check to make sure we don't include duplicates
                        if (characters.FindIndex(item => item == unicode) == -1)
                            characters.Add(unicode);
                    }
                    characterSet = characters.ToArray();
                    Debug.Log("Character count: " + characterSet.Length);

                    // Generate atlas
                    uint[] missingString = null;
                    fontAsset.atlasPopulationMode = AtlasPopulationMode.Dynamic;
                    fontAsset.ClearFontAssetData();
                    fontAsset.TryAddCharacters(characterSet, out missingString);

                    StringBuilder missingStrBuilder = new StringBuilder();
                    if (missingString != null)
                    {
                        foreach (uint unicode in missingString)
                        {
                            missingStrBuilder.Append(Convert.ToChar(unicode));
                            missingStrBuilder.Append(" ");
                        }
                        Debug.LogError($"<color=red>[Fatal error] </color>Missing string : {missingStrBuilder} ");
                    }
                    fontAsset.atlasPopulationMode = AtlasPopulationMode.Static;
                    TMPro_EventManager.ON_FONT_PROPERTY_CHANGED(true, fontAsset);
                    EditorUtility.SetDirty(fontAsset);
                }
            }

            EditorUtility.RequestScriptReload();
            isLoad = false;
        }

        private void SyncTablesWithGoogleSheets()
        {
            if (sheetsServiceProvider == null)
                return;

            foreach (var stringTableCollection in stringTableInfos)
            {
                var googleSheetsExtension = stringTableCollection.Extensions.OfType<GoogleSheetsExtension>().FirstOrDefault();
                if (googleSheetsExtension == null)
                {
                    Debug.LogWarning($"{stringTableCollection.name}: Google sheets extension doesn't exist");
                    continue;
                }

                EditorUtility.DisplayProgressBar("Pull sheet", string.Empty, 0);
                var googleSheets = new GoogleSheets(sheetsServiceProvider);
                googleSheets.SpreadSheetId = googleSheetsExtension.SpreadsheetId;
                googleSheets.PullIntoStringTableCollection
                    (
                        googleSheetsExtension.SheetId,
                        googleSheetsExtension.TargetCollection as StringTableCollection,
                        googleSheetsExtension.Columns,
                        googleSheetsExtension.RemoveMissingPulledKeys
                    );
                
                EditorUtility.ClearProgressBar();

                EditorUtility.DisplayProgressBar("Push sheet", string.Empty, 0);
                googleSheets.SpreadSheetId = googleSheetsExtension.SpreadsheetId;
                googleSheets.PushStringTableCollection
                    (
                        googleSheetsExtension.SheetId,
                        googleSheetsExtension.TargetCollection as StringTableCollection,
                        googleSheetsExtension.Columns
                    );

                EditorUtility.ClearProgressBar();
            }
        }

#if UNITY_EDITOR
        [CustomEditor(typeof(OneButtonLocalizedTMProFontAtlasGeneratorTool))]
        class Editor : UnityEditor.Editor
        {
            OneButtonLocalizedTMProFontAtlasGeneratorTool _target;
            new OneButtonLocalizedTMProFontAtlasGeneratorTool target => _target ?? (_target = (OneButtonLocalizedTMProFontAtlasGeneratorTool)base.target);

            public override void OnInspectorGUI()
            {
                if (GUILayout.Button("Generate"))
                    target.GenerateFontAtlas();

                serializedObject.Update();
                serializedObject.ApplyModifiedProperties();
                base.OnInspectorGUI();
            }
        }
#endif

        [Serializable]
        struct LocaleFontAssets
        {
            public string Name;
            public Locale Locale;
            public List<TMP_FontAsset> FontAssets;
        }
    }
}
