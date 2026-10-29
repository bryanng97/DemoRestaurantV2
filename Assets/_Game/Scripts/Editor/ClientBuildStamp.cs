using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
namespace DemoRestaurant.Editor
{
    /// <summary>Embed the actual native build number used by compatibility headers.</summary>
    public sealed class ClientBuildStamp : IPreprocessBuildWithReport
    {
        public int callbackOrder=>0;
        public void OnPreprocessBuild(BuildReport report)
        {
            string number=report.summary.platform==BuildTarget.Android?PlayerSettings.Android.bundleVersionCode.ToString():
                report.summary.platform==BuildTarget.iOS?PlayerSettings.iOS.buildNumber:"0";
            if(!int.TryParse(number,out int build)||build<0)throw new BuildFailedException("Use a non-negative numeric mobile build number.");
            if(!System.Text.RegularExpressions.Regex.IsMatch(Application.version,"^[0-9]{1,4}\\.[0-9]{1,4}\\.[0-9]{1,4}$"))throw new BuildFailedException("Asset release compatibility requires a three-part numeric app version.");
            Directory.CreateDirectory("Assets/_Game/Resources/Online");
            File.WriteAllText("Assets/_Game/Resources/Online/ClientBuild.json","{\"number\":\""+number+"\"}");
            AssetDatabase.ImportAsset("Assets/_Game/Resources/Online/ClientBuild.json");
        }
    }
}
