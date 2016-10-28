using System;
using System.IO;
using System.Linq;
using System.Globalization;
using System.Collections.Generic;

using UnityEngine;

namespace DockingFuelPump
{
	public class TestHighlight : PartModule
	{

		[KSPEvent(guiActive = true, guiName = "highlight")]
		public void test_highligh(){
			Debug.Log("Start Pump (out)");
			Events["test_highligh"].active = false;
			Events["clear_highlight"].active = true;


			this.part.Highlight(Color.red);
			this.part.parent.Highlight(Color.blue);
			foreach(Part p in this.part.children){
				p.Highlight(Color.green);
			}

		}

		[KSPEvent(guiActive = true, guiName = "clear highlight", active = false)]
		public void clear_highlight(){
			Debug.Log("Stop Pump (out)");
			Events["test_highligh"].active = true;
			Events["clear_highlight"].active = false;
			foreach(Part part in FlightGlobals.ActiveVessel.parts){
				part.Highlight(false);
			}

		}

	}



	public class DockingFuelPump : PartModule
	{
		public void log(string msg){
			Debug.Log("[DFP] " + msg);
		}

		[KSPEvent(guiActive = true, guiName = "Pump Fuel (out)")]
		public void pump_out(){
			Debug.Log("Start Pump (out)");
			Events["pump_out"].active = false;
			Events["stop_pump_out"].active = true;
			pump_fuel();
		}

		[KSPEvent(guiActive = true, guiName = "Stop Pump", active = false)]
		public void stop_pump_out(){
			Debug.Log("Stop Pump (out)");
			Events["pump_out"].active = true;
			Events["stop_pump_out"].active = false;
			foreach(Part part in FlightGlobals.ActiveVessel.parts){
				part.Highlight(false);
			}

		}

		[KSPEvent(guiActive = true, guiName = "Pump Fuel (in)")]
		public void pump_in(){
			Debug.Log("Start Pump (in)");
			Events["pump_in"].active = false;
			Events["stop_pump_in"].active = true;
			start_pump_in();
		}

		[KSPEvent(guiActive = true, guiName = "Stop Pump", active = false)]
		public void stop_pump_in(){
			Debug.Log("Stop Pump (in)");
			Events["pump_in"].active = true;
			Events["stop_pump_in"].active = false;
		}

		public void pump_fuel(){
			Part connected_part; 
			connected_part = this.part.attachNodes[1].attachedPart; //get part attached to lower node on docking port
			if(!connected_part){
				connected_part = this.part.srfAttachNode.attachedPart; //get part port is surface attached to
			}


			List<Part> south_parts = new List<Part>();
			List<Part> new_parts = new List<Part>();
			List<Part> next_parts = new List<Part>();
			List<int> part_ids = new List<int>();

			new_parts.Add(connected_part);
			south_parts.Add(this.part);

			bool itterate = true;

			log("starting....");

			while(itterate){
				next_parts.Clear();
				part_ids.Clear();

				foreach(Part part in south_parts){
					part_ids.Add(part.GetInstanceID());
				}

				foreach(Part part in new_parts){
					if (part.parent) {
						next_parts.Add(part.parent);
					}
					foreach(Part p in part.children){
						next_parts.Add(p);
					}
				}

				new_parts.Clear();
				if (next_parts.Count > 0) {
					foreach (Part part in next_parts) {
						if (!part_ids.Contains(part.GetInstanceID())) {
							new_parts.Add(part);
							south_parts.Add(part);
						}
					}
				}else{
					itterate = false;
				}
					
			}



			Debug.Log("South Parts - " + south_parts.Count);
			foreach(Part part in south_parts){
				Debug.Log(part.name);
				part.Highlight(Color.green);
			}





//			foreach(Part part in FlightGlobals.ActiveVessel.parts){
//				if (part == connected_part){
//					log(part.GetInstanceID().ToString());
//				}
//			}



//			log(this.part.dockingPorts.Count.ToString());
//			log(this.part.dockingPorts[0].ToString());


//			List<Part> parts = FlightGlobals.ActiveVessel.parts;
//			foreach(Part part in parts){
//				if(part.hasIndirectParent(this.part)){
//					south_parts.Add(part);
//				}else{
//					north_parts.Add(part);
//				}
//			}
//
//			Debug.Log("North Parts");
//			foreach(Part part in north_parts){
//				Debug.Log(part.name);
//			}


		}

		public void start_pump_in(){
			Part connected_part; 
			connected_part = this.part.attachNodes[1].attachedPart; //get part attached to lower node on docking port
			if(!connected_part){
				connected_part = this.part.srfAttachNode.attachedPart; //get part port is surface attached to
			}

			connected_part.Highlight(Color.red);
			connected_part.parent.Highlight(Color.blue);
			foreach(Part p in connected_part.children){
				p.Highlight(Color.green);
			}
		}
	}
}

