using System;
using System.IO;
using System.Linq;
using System.Globalization;
using System.Collections.Generic;

using UnityEngine;

namespace DockingFuelPump
{

    [KSPAddon(KSPAddon.Startup.MainMenu, false)]
    public class DFPSettings : MonoBehaviour
    {
        private void Awake(){
            try{
                
                string settings = System.IO.File.ReadAllText(Paths.joined(KSPUtil.ApplicationRootPath, "GameData", "DockingFuelPump", "dfp_settings.cfg"));
                string[] lines = settings.Split(new string[] { Environment.NewLine }, StringSplitOptions.None);
                Dictionary<string, string> opts = new Dictionary<string, string>();
                foreach(string line in lines){
                    if( !(line.StartsWith("{") || line.StartsWith("}") || line.StartsWith("//") || String.IsNullOrEmpty(line)) ){
                        string[] l = line.Split('=');
                        l[1] = l[1].Split(new string[] {"//"}, StringSplitOptions.None)[0];
                        opts.Add(l[0].Trim(), l[1].Trim());
                    }
                }
                if(opts.ContainsKey("flow_rate")){
                    DockingFuelPump.flow_rate = double.Parse(opts["flow_rate"]);
                }
                if(opts.ContainsKey("power_drain")){
                    DockingFuelPump.power_drain = double.Parse(opts["power_drain"]);
                }
                if(opts.ContainsKey("transfer_highlighting")){
                    DockingFuelPump.part_highlighting = bool.Parse(opts["transfer_highlighting"]);
                }
                if(opts.ContainsKey("transfer_heating")){
                    DockingFuelPump.transfer_heating = bool.Parse(opts["transfer_heating"]);
                }
            }
            catch{
                Debug.Log("[DFP] loading settings failed, using defaults");
            }

        }
    }

    public class ClawFuelPump : DockingFuelPump{

        public override void get_docked_info(){
            docked_to = find_attached_part();
            if(docked_to){
                is_docked = true;
            }
        }

        internal Part find_attached_part(){
            Part attached_part = null;
            ModuleGrappleNode module = this.part.FindModuleImplementing<ModuleGrappleNode>();
            if(module.otherVesselInfo != null){
                foreach(Part part in FlightGlobals.ActiveVessel.parts){
                    if(part.flightID == module.dockedPartUId){
                        attached_part = part;
                    }
                }
            }
            return attached_part;
        }

    }

    public class DockingFuelPump : PartModule
    {

        internal static double flow_rate = 1.0;
        internal static double power_drain = 0.3;
        internal static bool part_highlighting = false;
        internal static bool transfer_heating = true;

        //North and South Parts; parts divided in relationship to the docking port. Fuel will be pumped from the south to the north.
        internal List<Part> north_parts = new List<Part>(); //Parts "North" of the docking port are those which are connected via a docking join
        internal List<Part> south_parts = new List<Part>();    //Parts "South" of the docking port are those which are connected via the attachment node on the docking port
        internal Dictionary<string, List<PartResource>> source_resources = new Dictionary<string, List<PartResource>>();


        internal bool is_docked = false;
        internal Part docked_to;
        internal DockingFuelPump opposite_pump;

        public bool pump_running = false;
        public bool warning_displayed = false;



        [KSPEvent(guiActive = true, guiName = "Pump Fuel")]
        public void pump_out(){
            start_fuel_pump();
        }

        [KSPEvent(guiActive = true, guiName = "Stop Pump", active = false)]
        public void stop_pump_out(){
            stop_fuel_pump();
        }

        public void start_fuel_pump(){
            is_docked = false;
            warning_displayed = false;

            get_docked_info();

            if(is_docked){
                log("Starting  Pump");
                Events["pump_out"].active = false;
                Events["stop_pump_out"].active = true;

                south_parts = get_descendant_parts(this.part);
                north_parts = get_descendant_parts(docked_to);
                identify_source_resources();
                highlight_parts();
                pump_running = true;
            }
        }
    
        public void stop_fuel_pump(){
            Events["pump_out"].active = true;
            Events["stop_pump_out"].active = false;
            pump_running = false;
            log("Pump stopped");
            unhighlight_parts();
        }


        //set info about the docking.  if the part is docked this set's is_docked to true and sets docked_to to be the part it is docked with.
        public virtual void get_docked_info(){
            ModuleDockingNode module = this.part.FindModuleImplementing<ModuleDockingNode>();
            if(module.otherNode){
                docked_to = module.otherNode.part;
                opposite_pump = docked_to.FindModuleImplementing<DockingFuelPump>();
                is_docked = true;
            }
        }

        //Find the part(s) the focal_part (typically the docking port) is attached to (either by node or surface attach), 
        //but not the parts it is docked to.
        public List<Part> get_connected_parts(Part focal_part){
            List<Part> connected_parts = new List<Part>(); 
            foreach(AttachNode node in focal_part.attachNodes){
                if(node.attachedPart){
                    connected_parts.Add(node.attachedPart);
                }
            }
            if(focal_part.srfAttachNode.attachedPart){
                connected_parts.Add(focal_part.srfAttachNode.attachedPart);
            }
            connected_parts.Remove(docked_to);
            return connected_parts;
        }


        public List<Part> get_descendant_parts(Part focal_part){
            List<Part> descendant_parts = new List<Part>();
            List<Part> next_parts = new List<Part>();
            List<Part> connected_parts = get_connected_parts(focal_part);


            //Walk the tree structure of connected parts to recursively discover parts which fall on one side (the south side) of the focal part.
            //The starting point is the parts in connected_parts.  By adding the focal part to descendant_parts they are excluded
            //which acts to block the discovery of parts in one direction
            descendant_parts.Add(focal_part);    
            
            bool itterate = true;
            while(itterate){
                next_parts.Clear();

                //select parents and children of parts in connected_parts and add them to next_parts.
                foreach(Part part in connected_parts){
                    if (part.parent) {
                        next_parts.Add(part.parent);
                    }
                    foreach (Part p in part.children) {
                        next_parts.Add(p);
                    }
                }

                //add any parts in next_parts which are not already in descendant_parts to descendant_parts and new_parts
                //if next_parts is empty then exit the loop.
                connected_parts.Clear();
                itterate = false;
                if (next_parts.Count > 0) {
                    itterate = true;
                    foreach (Part part in next_parts) {
                        bool allow_flow = part.fuelCrossFeed || part.FindModuleImplementing<ModuleGrappleNode>();
                        if(!part_ids_for(descendant_parts).Contains(part.GetInstanceID()) && allow_flow){
                            connected_parts.Add(part);
                            descendant_parts.Add(part);
                        }
                    }
                }

                //ensure the loop will end if the above check fails.
                if(descendant_parts.Count > FlightGlobals.ActiveVessel.parts.Count){ 
                    itterate = false;
                }
            }

            //filter descendant_parts to be just those which have resources.
            List<int> part_ids = part_ids_for(descendant_parts);
            descendant_parts.Clear();
            foreach(Part part in FlightGlobals.ActiveVessel.parts){
                if (part.Resources.Count > 0) {
                    if (part_ids.Contains(part.GetInstanceID())) {
                        descendant_parts.Add(part);
                    }
                }
            }
            return descendant_parts;
        }


        //Get array of IDs for all parts in a list of parts
        public List<int> part_ids_for(List<Part> parts){
            List<int> part_ids = new List<int>();
            foreach(Part part in parts){
                part_ids.Add(part.GetInstanceID());
            }
            return part_ids;
        }


        //setup dictionary of resource name to list of available PartResource(s) on the source (south) parts.
        internal void identify_source_resources(){
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



        public override void OnUpdate(){
            //TODO stop pump on undock
            //TODO stop pump on explode 
            if(pump_running){
                double resources_transfered = 0; //keep track of overall quantity of resources transfered across the docking port each cycle. used to auto stop the pump.

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
                double per_tank_flow = (flow_rate * 4) / required_resources.Count;
                //                per_tank_flow = per_tank_flow * this.part.aerodynamicArea;  //factor size of docking port in rate of flow
                //                per_tank_flow = per_tank_flow * TimeWarp.deltaTime;         //factor in physics warp


                //Transfer of resources from South (source) parts to North (sink) parts.
                foreach(Part sink_part in north_parts){
                    foreach(PartResource resource in sink_part.Resources){
                        if(resource.resourceName != "ElectricCharge" && resource.amount < resource.maxAmount && source_resources.ContainsKey(resource.resourceName)){
                            List<PartResource> resources = source_resources[resource.resourceName];

                            double required_res = resource.maxAmount - resource.amount;         //the total amount of resource needed in this resource tank
                            double avail_res = 0;
                            foreach(PartResource res in resources){avail_res += res.amount;}    //the total amount of resource available in the source tanks.
                            //select the smallest amount from availabe, required and the per_tank_flow limit
                            double to_transfer = new double[]{required_res, avail_res, per_tank_flow}.Min();

                            resource.amount += to_transfer; //add quantity of resource to tank
                            resources_transfered += to_transfer;  //and add quantity to track of total resources transfered in this cycle.
                            //deduct quantity from source tanks, dividing the quantity from the available source tanks.
                            int r_count = resources.Count;
                            foreach(PartResource res in resources){
                                double max_out = new double[]{ to_transfer/r_count, res.amount }.Min();
                                res.amount -= max_out;
                                to_transfer -= max_out;
                                r_count -= 1;
                            }
                            resource.amount -= to_transfer; //handles case where not all of the to_transfer demand was completely shared but the source resources.
                        }
                    }
                }

                //Docking Port heating
                if(transfer_heating){
                    if(this.part.temperature < this.part.maxTemp * 0.7){
                        this.part.temperature += 50;
                    }
                    if(docked_to.temperature < docked_to.maxTemp * 0.7){
                        docked_to.temperature += 50;
                    }
                }


                //Docking Port overheating when adjacent ports are both pumping (will quickly overheat and explode ports).
                if(opposite_pump && opposite_pump.pump_running){
                    if(!warning_displayed){
                        log("opposite pump running - imminent KABOOM likely!");
                        warning_displayed = true;
                    }
                    this.part.temperature += 20;
                    docked_to.temperature += 20;
                }

                //pump shutdown when dry.
                log("transfered: " + resources_transfered);
                if(resources_transfered < 0.01){
                    stop_fuel_pump();
                }

                //pump power draw and shutdown when out of power.
                if(power_drain > 0 && resources_transfered > 0){
                    if(this.part.RequestResource("ElectricCharge", power_drain * resources_transfered) <= 0){
                        stop_fuel_pump();
                    }
                }
            }
        }



        //adds different highlight to south and north parts
        public void highlight_parts(){
            if (part_highlighting) {
                foreach (Part part in north_parts) {
                    part.Highlight(Color.blue);
                }
                foreach (Part part in south_parts) {
                    part.Highlight(Color.green);
                }            
            }
        }

        //remove highlighting from parts.
        public void unhighlight_parts(){
            if(part_highlighting){
                foreach(Part part in FlightGlobals.ActiveVessel.parts){
                    part.Highlight(false);
                }
            }

        }

        //debug log shorthand
        public void log(string msg){
            Debug.Log("[DFP] " + msg);
        }

    }








    public class TestHighlight : PartModule
    {
        public void log(string msg){
            Debug.Log("[DFP] " + msg);
        }

        internal bool is_docked = false;
        internal Part docked_to;
        internal DockingFuelPump opposite_pump;

        [KSPEvent(guiActive = true, guiName = "highlight")]
        public void test_highligh(){
            Events["test_highligh"].active = false;
            Events["clear_highlight"].active = true;


            this.part.Highlight(Color.red);
            this.part.parent.Highlight(Color.blue);
            foreach(Part part in this.part.children){
                part.Highlight(Color.green);
            }

            log(this.part.fuelCrossFeed.ToString());


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


