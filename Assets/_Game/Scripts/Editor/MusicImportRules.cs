using UnityEditor;
using UnityEngine;

namespace DemoRestaurant.EditorTools
{
    /// <summary>Long background tracks stream from disk instead of keeping decoded audio in memory.</summary>
    public sealed class MusicImportRules : AssetPostprocessor
    {
        private void OnPreprocessAudio()
        {
            if (!assetPath.StartsWith("Assets/_Game/Resources/Audio/Music/") && !assetPath.StartsWith("Assets/_Game/Resources/Audio/RefProto/Music/")) return;
            var importer = (AudioImporter)assetImporter;
            var settings = importer.defaultSampleSettings;
            settings.loadType = AudioClipLoadType.Streaming;
            settings.compressionFormat = AudioCompressionFormat.Vorbis;
            settings.quality = 0.7f;
            importer.defaultSampleSettings = settings;
            importer.loadInBackground = true;
            importer.forceToMono = false;
        }
    }
}
