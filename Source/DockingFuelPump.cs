﻿using System;
using System.IO;
using System.Linq;
using System.Globalization;
using System.Collections.Generic;

using UnityEngine;

namespace DockingFuelPump
{

    public class DockingFuelPump : PartModule
    {
        
        public void log(string msg){
            Debug.Log("[DFP] " + msg);
        }


        [KSPEvent(guiActive = true, guiName = "Pump Fuel")]
        public void pump_out(){
            start_fuel_pump();
        }

        [KSPEvent(guiActive = true, guiName = "Stop Pump", active = false)]
        public void stop_pump_out(){
            stop_pump();
        }

        //North and South Parts; parts divided in relationship to the docking port. Fuel will be pumped from the south to the north.
        internal List<Part> north_parts = new List<Part>(); //Parts "North" of the docking port are those which are connected via a docking join
        internal List<Part> south_parts = new List<Part>();    //Parts "South" of the docking port are those which are connected via the attachment node on the docking port
        internal List<int> part_ids = new List<int>();        //List of part ids, used in checking which parts have been added to south_parts.
        internal Dictionary<string, List<PartResource>> source_resources = new Dictionary<string, List<PartResource>>();

        internal bool is_docked = false;
        internal Part docked_to;
        internal DockingFuelPump opposite_pump;

        public bool pump_running = false;
        public bool warning_displayed = false;
        internal double flow_rate = 1.0;
        internal double power_drain = 0.3;

        public void start_fuel_pump(){

            is_docked = false;
            warning_displayed = false;

            ModuleDockingNode node = this.part.FindModuleImplementing<ModuleDockingNode>();
            if(node.otherNode){
                docked_to = node.otherNode.part;
                opposite_pump = docked_to.FindModuleImplementing<DockingFuelPump>();
                is_docked = true;
            }

            if(is_docked){
                Debug.Log("Starting  Pump");
                Events["pump_out"].active = false;
                Events["stop_pump_out"].active = true;

                identify_south_parts();
                north_parts = opposite_pump.identify_south_parts();
                identify_south_resources();

                highlight_parts();
                pump_running = true;
            }
        }
    
        public void stop_pump(){
            Events["pump_out"].active = true;
            Events["stop_pump_out"].active = false;
            pump_running = false;
            Debug.Log("Pump stopped");
        }



        public override void OnUpdate(){
            //TODO stop pump on undock
            if(pump_running){
                double resources_transfered = 0;


                //Calculate the number of resouce tanks that will be transfering resources to determine the per tank flow rate
                //the aim being to have a max flow rate across the docking port. per tank transfer speed will increase as fewer tanks are engaged in transfer and vice versa.
                List<string> required_resources = new List<string>();
                List<string> avail_resources = new List<string>();
                foreach (KeyValuePair<string, List<PartResource>> res_list in source_resources) {
                    foreach(PartResource res in res_list.Value){
                        if(res.resourceName != "ElectricCharge" && res.amount > 0.01){
                            avail_resources.Add(res.resourceName);
                        }
                    }
                }
                foreach (Part sink_part in north_parts) {
                    foreach (PartResource resource in sink_part.Resources) {
                        if (resource.resourceName != "ElectricCharge" && resource.amount < resource.maxAmount && avail_resources.Contains(resource.resourceName)) {
                            required_resources.Add(resource.resourceName);
                        }
                    }
                }
                    
                int transfer_count = new int[]{ required_resources.Count, avail_resources.Count }.Min();

                double per_tank_flow = flow_rate / transfer_count;
//                per_tank_flow = per_tank_flow * TimeWarp.deltaTime;
//                log("transfer count: " + transfer_count + " tank flow: " + per_tank_flow);


                //Transfer of resources from South (source) parts to North (sink) parts.
                foreach(Part sink_part in north_parts){
                    foreach(PartResource resource in sink_part.Resources){
                        if(resource.resourceName != "ElectricCharge" && resource.amount < resource.maxAmount && source_resources.ContainsKey(resource.resourceName)){
                            List<PartResource> resources = source_resources[resource.resourceName];

                            double required_res = resource.maxAmount - resource.amount;
                            double avail_res = 0;
                            foreach(PartResource res in resources){avail_res += res.amount;}
                            double to_transfer = new double[]{required_res, avail_res, per_tank_flow}.Min();

                            resource.amount += to_transfer;
                            resources_transfered += to_transfer;
                            int r_count = resources.Count;
                            foreach(PartResource res in resources){
                                double max_out = new double[]{ to_transfer/r_count, res.amount }.Min();
                                res.amount -= max_out;
                                to_transfer -= max_out;
                                r_count -= 1;
                            }
                            resource.amount -= to_transfer; //handles case where not all of the to_transfer demand was completely shared but the source resources.
                            resources_transfered -= to_transfer;

                        }
                    }
                }

                //Docking Port heating
                if(this.part.temperature < this.part.maxTemp * 0.8){
                    this.part.temperature += 10;
                }
                if(docked_to.temperature < docked_to.maxTemp * 0.8){
                    docked_to.temperature += 10;
                }


                //Docking Port overheating when adjacent ports are both pumping (will quickly overheat and explode ports).
                if(opposite_pump.pump_running){
                    if(!warning_displayed){
                        log("opposite pump running - imminent KABOOM likely!");
                        warning_displayed = true;
                    }
                    this.part.temperature += 10;
                    docked_to.temperature += 10;
                }

                //pump shutdown when dry.
                log("transfered: " + resources_transfered);
                if(resources_transfered < 0.1){
                    stop_pump();
                }

                //pump power draw and shutdown when out of power.
//                double p = this.part.RequestResource("ElectricCharge", power_drain * resources_transfered);
                if(this.part.RequestResource("ElectricCharge", power_drain * resources_transfered) <= 0){
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

        public List<Part> identify_south_parts(){
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
                new_parts.Add(part);     //add the part the docking port is connected to as the starting point
            }
            south_parts.Add(this.part);    //south_parts include the docking port so it will be excluded from subsequent passes

            //starting with the connected_part (in new_parts) find it's parent and children parts and add them to next_parts. 
            //once itterated over new_parts the parent and children parts which were added to next_parts are added to new_parts and south_parts 
            //so long as they are not already in south parts.  The loop then repeats with the new set of new_parts.  When no more parts are added to 
            //next_parts the loop is stopped.
            //in other words, walking the tree structure to recursively discover parts which fall on one side (the south side) of the docking port.
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
                        if(!south_part_ids().Contains(part.GetInstanceID()) && part.fuelCrossFeed){
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

            //filter south parts to just those with resources.
            south_part_ids();
            south_parts.Clear();
            foreach(Part part in FlightGlobals.ActiveVessel.parts){
                if (part.Resources.Count > 0) {
                    if (part_ids.Contains(part.GetInstanceID())) {
                        south_parts.Add(part);
                    }
                }
            }

            return south_parts;

        }

        public void highlight_parts(){
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
    }



    public class TestHighlight : PartModule
    {
        public void log(string msg){
            Debug.Log("[DFP] " + msg);
        }

        [KSPEvent(guiActive = true, guiName = "highlight")]
        public void test_highligh(){
            Events["test_highligh"].active = false;
            Events["clear_highlight"].active = true;

//            Part connected_part; 
//            connected_part = this.part.attachNodes[1].attachedPart;     //get part attached to lower node on docking port
//            if(!connected_part){
//                connected_part = this.part.srfAttachNode.attachedPart;     //get part port is surface attached to
//            }
//            connected_part.Highlight(Color.red);
//
//            this.part.Highlight(Color.red);
//            this.part.parent.Highlight(Color.blue);
//            foreach(Part p in this.part.children){
//                p.Highlight(Color.green);
//            }

//            ModuleDockingNode node = this.part.FindModuleImplementing<ModuleDockingNode>();
//            log((node.otherNode == null).ToString());
//            node.otherNode.part.Highlight(Color.magenta);

            foreach(PartResource res in this.part.Resources){
                log("name: " + res.resourceName + " amount: " + res.amount);
            }

//            DockingFuelPump pump = this.part.FindModuleImplementing<DockingFuelPump>();
//            bool running = pump.pump_running;



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


}


