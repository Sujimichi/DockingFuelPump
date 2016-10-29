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

    public class DockingFuelPump : PartModule
    {

        internal static double flow_rate = 1.0;
        internal static double power_drain = 0.3;
        internal static bool part_highlighting = false;
        internal static bool transfer_heating = true;

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



        [KSPEvent(guiActive = true, guiName = "Pump Fuel")]
        public void pump_out(){
            start_fuel_pump();
        }

        [KSPEvent(guiActive = true, guiName = "Stop Pump", active = false)]
        public void stop_pump_out(){
            stop_pump();
        }

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
                log("Starting  Pump");
                Events["pump_out"].active = false;
                Events["stop_pump_out"].active = true;

                identify_south_parts();
                north_parts = opposite_pump.identify_south_parts();
                identify_south_resources();

                if(part_highlighting){
                    highlight_parts();
                }
                pump_running = true;
            }
        }
    
        public void stop_pump(){
            Events["pump_out"].active = true;
            Events["stop_pump_out"].active = false;
            pump_running = false;
            if(part_highlighting){
                foreach(Part part in FlightGlobals.ActiveVessel.parts){
                    part.Highlight(false);
                }
            }
            log("Pump stopped");
        }



        public override void OnUpdate(){
            //TODO stop pump on undock
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
                double per_tank_flow = (flow_rate * 100) / required_resources.Count;
                per_tank_flow = per_tank_flow * this.part.aerodynamicArea;  //factor size of docking port in rate of flow
                per_tank_flow = per_tank_flow * TimeWarp.deltaTime;         //factor in physics warp


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
                    if(this.part.temperature < this.part.maxTemp * 0.8){
                        this.part.temperature += 10;
                    }
                    if(docked_to.temperature < docked_to.maxTemp * 0.8){
                        docked_to.temperature += 10;
                    }
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
                if(resources_transfered < 0.01){
                    stop_pump();
                }

                //pump power draw and shutdown when out of power.
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
            foreach(Part part in north_parts){
                part.Highlight(Color.blue);
            }
            
            foreach(Part part in south_parts){
                part.Highlight(Color.green);
            }            
        }

        public void log(string msg){
            Debug.Log("[DFP] " + msg);
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


