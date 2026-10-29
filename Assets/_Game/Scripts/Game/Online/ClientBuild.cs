using System;
using UnityEngine;
namespace DemoRestaurant.Game.Online
{
    public static class ClientBuild
    {
        [Serializable] private sealed class Info { public string number; }
        public static string Number
        {
            get
            {
                var text=Resources.Load<TextAsset>("Online/ClientBuild");
                string value=text==null?"0":JsonUtility.FromJson<Info>(text.text)?.number;
                return int.TryParse(value,out int parsed)&&parsed>=0?parsed.ToString(System.Globalization.CultureInfo.InvariantCulture):"0";
            }
        }
    }
}
