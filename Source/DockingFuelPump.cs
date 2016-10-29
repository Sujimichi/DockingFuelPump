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
			Events["test_highligh"].active = false;
			Events["clear_highlight"].active = true;

//			Part connected_part; 
//			connected_part = this.part.attachNodes[1].attachedPart; 	//get part attached to lower node on docking port
//			if(!connected_part){
//				connected_part = this.part.srfAttachNode.attachedPart; 	//get part port is surface attached to
//			}
//			connected_part.Highlight(Color.red);
//
//			this.part.Highlight(Color.red);
//			this.part.parent.Highlight(Color.blue);
//			foreach(Part p in this.part.children){
//				p.Highlight(Color.green);
//			}

//			ModuleDockingNode node = this.part.FindModuleImplementing<ModuleDockingNode>();
//			node.otherNode.part.Highlight(Color.magenta);

//			DockingFuelPump pump = this.part.FindModuleImplementing<DockingFuelPump>();
//			bool running = pump.pump_running;



		}

		[KSPEvent(guiActive = true, guiName = "clear highlight", active = false)]
		public void clear_highlight(){
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
			start_fuel_pump();
		}

		[KSPEvent(guiActive = true, guiName = "Stop Pump", active = false)]
		public void stop_pump_out(){
			stop_pump();
		}

		//North and South Parts; parts divided in relationship to the docking port. Fuel will be pumped from the south to the north.
		internal List<Part> north_parts = new List<Part>(); 	//Parts "North" of the docking port are those which are connected via a docking join
		internal List<Part> south_parts = new List<Part>();	//Parts "South" of the docking port are those which are connected via the attachment node on the docking port
		internal List<int> part_ids = new List<int>();		//List of part ids, used in checking which parts have been added to south_parts.

		internal Dictionary<string, List<PartResource>> source_resources = new Dictionary<string, List<PartResource>>();

		public bool pump_running = false;
		public bool warning_displayed = false;


		public void start_fuel_pump(){
			Debug.Log("Starting  Pump");
			Events["pump_out"].active = false;
			Events["stop_pump_out"].active = true;

			identify_parts();
			identify_south_resources();
			pump_running = true;
			warning_displayed = false;
		}
	
		public void stop_pump(){
			Events["pump_out"].active = true;
			Events["stop_pump_out"].active = false;
			pump_running = false;
			Debug.Log("Pump stopped");
		}


		public override void OnUpdate(){
			ModuleDockingNode node = this.part.FindModuleImplementing<ModuleDockingNode>();
			if(pump_running && node.otherNode){
				Part docked_to = node.otherNode.part;
				DockingFuelPump opposite_pump = docked_to.FindModuleImplementing<DockingFuelPump>();


				double resources_transfered = 0;
				foreach(Part sink_part in north_parts){
					foreach(PartResource resource in sink_part.Resources){
						if(resource.amount < resource.maxAmount && source_resources.ContainsKey(resource.resourceName)){
							List<PartResource> resources = source_resources[resource.resourceName];

							double required_res = resource.maxAmount - resource.amount;
							double avail_res = 0;
							foreach(PartResource res in resources){
								avail_res += res.amount;
							}
							double to_transfer = new double[]{required_res, avail_res, 5.0 * TimeWarp.deltaTime}.Min();

							resource.amount += to_transfer;
							resources_transfered += to_transfer;
							int r_count = resources.Count;
							foreach(PartResource res in resources){
								double max_out = new double[]{ to_transfer/r_count, res.amount }.Min();
								res.amount -= max_out;
								to_transfer -= max_out;
								r_count -= 1;
							}
						}
					}
				}
//
				if(this.part.thermalExposedFlux < 100){
					this.part.thermalExposedFlux += 50;
				}
				if(docked_to.thermalExposedFlux < 100){
					docked_to.thermalExposedFlux += 50;
				}

//				if(this.part.thermalInternalFlux < 600){
//					this.part.thermalInternalFlux += 400;
//				}
//				if(this.part.thermalSkinFlux < 400){
//					this.part.thermalSkinFlux += 80;
//				}
				if(opposite_pump.pump_running){
					if(!warning_displayed){
						log("opposite pump running - imminent KABOOM likely!");
						warning_displayed = true;
					}
					//						this.part.explode();
					//						docked_to.explode();
					if(this.part.thermalInternalFlux < 800){
						this.part.thermalInternalFlux += 600;
					}
					if(docked_to.thermalInternalFlux < 800){
						docked_to.thermalInternalFlux += 600;
					}
				}


				if(resources_transfered < 0.01){
					stop_pump();
				}
			}
		}



		//Get array of IDs of parts already added to south parts.
		public List<int> south_part_ids(){
			part_ids.Clear();
			foreach(Part part in south_parts){
				part_ids.Add(part.GetInstanceID());
			}
			return part_ids;
		}

		//setup dictionary of resource name to list of available Part resources on the south parts.
		internal void identify_south_resources(){
			source_resources = new Dictionary<string, List<PartResource>>();
			foreach(Part part in south_parts){
				foreach(PartResource res in part.Resources){
					if(!source_resources.ContainsKey(res.resourceName)){
						source_resources.Add(res.resourceName, new List<PartResource>());
					}
					source_resources[res.resourceName].Add(res);
				}
			}

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
				itterate = false;
				if (next_parts.Count > 0) {
					itterate = true;
					foreach (Part part in next_parts) {
						if(!south_part_ids().Contains(part.GetInstanceID())){
							new_parts.Add(part);
							south_parts.Add(part);
						}
					}
				}

				//ensure the loop will end if the above check fails.
				if(south_parts.Count > FlightGlobals.ActiveVessel.parts.Count){ 
					itterate = false;
				}

			}

			//get north parts as all vessel parts minus south parts and filter both to only include parts that have resources.
			south_part_ids();
			north_parts.Clear();
			south_parts.Clear();
			foreach(Part part in FlightGlobals.ActiveVessel.parts){
				if (part.Resources.Count > 0) {
					if (part_ids.Contains(part.GetInstanceID())) {
						south_parts.Add(part);
					} else {
						north_parts.Add(part);
					
					}
				}
			}


		}

	}
}

//			Debug.Log("North Parts - " + north_parts.Count);
//			foreach(Part part in north_parts){
//				Debug.Log(part.name);
//				part.Highlight(Color.blue);
//			}
//
//			Debug.Log("South Parts - " + south_parts.Count);
//			foreach(Part part in south_parts){
//				Debug.Log(part.name);
//				part.Highlight(Color.green);
//			}
