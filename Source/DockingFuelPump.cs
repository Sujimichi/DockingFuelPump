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
		public void log(string msg){
			Debug.Log("[DFP] " + msg);
		}

		[KSPEvent(guiActive = true, guiName = "highlight")]
		public void test_highligh(){
			Debug.Log("Start Pump (out)");
			Events["test_highligh"].active = false;
			Events["clear_highlight"].active = true;


			foreach(PartResource r in this.part.Resources){
				log(r.resourceName + " max: " + r.maxAmount + " cur: " + r.amount);
			}


//			Part connected_part; 
//			connected_part = this.part.attachNodes[1].attachedPart; 	//get part attached to lower node on docking port
//			if(!connected_part){
//				connected_part = this.part.srfAttachNode.attachedPart; 	//get part port is surface attached to
//			}
//			connected_part.Highlight(Color.red);

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

		List<Part> north_parts = new List<Part>();
		List<Part> south_parts = new List<Part>();


		public void pump_fuel(){

			identify_parts();

			Debug.Log("North Parts - " + north_parts.Count);
			foreach(Part part in north_parts){
				Debug.Log(part.name);
				part.Highlight(Color.blue);
			}

			Debug.Log("South Parts - " + south_parts.Count);
			foreach(Part part in south_parts){
				Debug.Log(part.name);
				part.Highlight(Color.green);
			}



		}

		//Get array of IDs of parts already added to south parts.
		List<int> part_ids = new List<int>();
		public List<int> south_part_ids(){
			part_ids.Clear();
			foreach(Part part in south_parts){
				part_ids.Add(part.GetInstanceID());
			}
			return part_ids;
		}

		internal void identify_parts(){
			//Find the part(s) the docking port is attached to (either by node or surface attach)
			List<Part> connected_parts = new List<Part>(); 
			foreach(AttachNode node in this.part.attachNodes){
				if(node.attachedPart){
					connected_parts.Add(node.attachedPart);
				}
			}
			if(this.part.srfAttachNode.attachedPart){
				connected_parts.Add(this.part.srfAttachNode.attachedPart);
			}


			//Find all the parts which are "south" of the docking port". South means parts which are connected to the non-docking side of the docking port.
			List<Part> new_parts = new List<Part>();  //used in intterating over associated parts, two lists are used so one can be modified while itterating over the other.
			List<Part> next_parts = new List<Part>();


			south_parts.Clear();
			foreach(Part part in connected_parts){
				new_parts.Add(part); 	//add the part the docking port is connected to as the starting point
			}
			south_parts.Add(this.part);	//south_parts include the docking port so it will be excluded from subsequent passes

			//starting with the connected_part (in new_parts) find it's parent and children parts and add them to next_parts. 
			//once itterated over new_parts the parent and children parts which were added to next_parts are added to new_parts and south_parts 
			//so long as they are not already in south parts.  The loop then repeats with the new set of new_parts.  When no more parts are added to 
			//next_parts the loop is stopped.
			bool itterate = true;
			while(itterate){
				next_parts.Clear();

				//select parents and children of parts in new_parts and add them to next_parts.
				foreach(Part part in new_parts){
					if (part.parent) {
						next_parts.Add(part.parent);
					}
					foreach(Part p in part.children){
						next_parts.Add(p);
					}
				}

				//add any parts in next_parts which are not already in south_parts to south_parts and new_parts
				//if next_parts is empty then exit the loop.
				new_parts.Clear();
				if (next_parts.Count > 0) {
					foreach (Part part in next_parts) {
						if(!south_part_ids().Contains(part.GetInstanceID())){
							new_parts.Add(part);
							south_parts.Add(part);
						}
					}
				}else{
					itterate = false;
				}

				//ensure the loop will end if the above check fails.
				if(south_parts.Count > FlightGlobals.ActiveVessel.parts.Count){ 
					itterate = false;
				}

			}

			north_parts.Clear();
			foreach(Part part in FlightGlobals.ActiveVessel.parts){
				if(!south_part_ids().Contains(part.GetInstanceID())){
					north_parts.Add(part);
				}
			}


		}

	}
}

