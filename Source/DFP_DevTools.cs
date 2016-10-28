using System;
using System.Linq;
using System.Globalization;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.IO;
using UnityEngine;
using KSP;
using KSP.UI;


namespace DockingFuelPump
{
	
    [KSPAddon(KSPAddon.Startup.MainMenu, true)]
    public class JumpStart : MonoBehaviour
    {
        public bool autostart = true;
        public string save_name = "default";
//        public string mode = "spacecenter";
//        public string mode = "editor";
		public string mode = "flight";
        public string craft_name = "DockingPortTest";

        public void Start(){

            if(autostart){
                HighLogic.SaveFolder = save_name;
                DebugToolbar.toolbarShown = true;

                if(mode == "editor"){
                    var editor = EditorFacility.VAB;
                    GamePersistence.LoadGame("persistent", HighLogic.SaveFolder, true, false);
                    if(craft_name != null || craft_name != ""){					
                        string path = Paths.joined(KSPUtil.ApplicationRootPath, "saves", save_name, "Ships", "VAB", craft_name + ".craft");
                        EditorDriver.StartAndLoadVessel(path, editor);
                    } else{
                        EditorDriver.StartEditor(editor);
                    }
                } else if(mode == "spacecenter"){
                    HighLogic.LoadScene(GameScenes.SPACECENTER);
				}else if(mode == "flight"){
					FlightDriver.StartAndFocusVessel("quicksave", 1);
				}

            }
        }
    }

	public class Paths
	{
		//takes any number of strings and returns them joined together with OS specific path divider, ie:
		//Paths.joined("follow", "the", "yellow", "brick", "road") -> "follow/the/yellow/brick/road or follow\the\yellow\brick\road
		//actually, not true, now it just joins them with /, as it seems mono sorts out path dividers anyway and using \ just messes things up here. (I mean, what kinda os uses \ anyway, madness).
		static public string joined(params string[] paths){
			string path = paths[0];
			for(int i = 1; i < paths.Length; i++){
				path = path + "/" + paths[i];
			}
			return path;
		}
	}


}

