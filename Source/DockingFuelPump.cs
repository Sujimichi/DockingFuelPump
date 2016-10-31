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
        internal Dictionary<string, List<PartResource>> source_resources = new Dictionary<string, List<PartResource>>();
        internal Dictionary<string, List<PartResource>> sink_resources = new Dictionary<string, List<PartResource>>();


        internal bool is_docked = false;
        internal Part docked_to;
        internal DockingFuelPump opposite_pump;
        internal bool reverse_pump = false;

        public bool pump_running = false;
        public bool warning_displayed = false;



        [KSPEvent(guiActive = true, guiName = "Pump Fuel")]
        public void pump_out(){
            reverse_pump = false;
            start_fuel_pump();
        }

        [KSPEvent(guiActive = true, guiName = "Stop Pump", active = false)]
        public void stop_pump_out(){
            stop_fuel_pump();
        }

        //setup events to stop the fuel pump when the port is undocked or it goes kaboomy
        public override void OnStart(StartState state){
            this.part.OnJustAboutToBeDestroyed += () => {
                if(pump_running){
                    stop_fuel_pump();
                    if(opposite_pump){
                        opposite_pump.stop_fuel_pump();
                    }
                }
            };
            GameEvents.onPartUndock.Add((part) => {
                DockingFuelPump module = part.FindModuleImplementing<DockingFuelPump>();
                if(module){
                    log("undocking...pump stop");
                    module.stop_fuel_pump();
                }
            });
        }


        public virtual void start_fuel_pump(){
            is_docked = false;
            warning_displayed = false;

            get_docked_info();
            if(is_docked){
                log("Starting Fuel Pump");
                Events["pump_out"].active = false;
                Events["stop_pump_out"].active = true;
                get_part_groups();
                source_resources = identify_resources_in_parts(south_parts);
                sink_resources   = identify_resources_in_parts(north_parts);
                highlight_parts();
                pump_running = true;
            }
        }
    
        public virtual void stop_fuel_pump(){
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

        //get vessl parts (just those with resources) in two groups. Those on the opposite side of this.part to the part it is docked to (the south parts)
        //and those on (and connected to) the part this.part is docked to (the north parts). 
        public virtual void get_part_groups(){
            south_parts = get_descendant_parts(this.part);
            north_parts = get_descendant_parts(docked_to);
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

        //Walk the tree structure of connected parts to recursively discover parts which are on the side of the given focal part opposite to the part it is docked to.
        //Walking the tree is blocked by parts which do not have cross feed enabled.
        public List<Part> get_descendant_parts(Part focal_part){
            List<Part> descendant_parts = new List<Part>();
            List<Part> next_parts = new List<Part>();
            List<Part> connected_parts = get_connected_parts(focal_part);

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


        //setup dictionary of resource name to list of available PartResource(s) in the given list of parts
        internal Dictionary<string, List<PartResource>> identify_resources_in_parts(List<Part> parts){
            Dictionary<string, List<PartResource>> resources = new Dictionary<string, List<PartResource>>();
            foreach(Part part in parts){
                foreach(PartResource res in part.Resources){
                    if(!resources.ContainsKey(res.resourceName)){
                        resources.Add(res.resourceName, new List<PartResource>());
                    }
                    resources[res.resourceName].Add(res);
                }
            }
            return resources;
        }

        public override void OnUpdate(){
            if(pump_running){
                double resources_transfered = 0; //keep track of overall quantity of resources transfered across the docking port each cycle. used to auto stop the pump.

                //find the types of resources which need to be transfered
                List<string> required_resource_types = new List<string>();
                foreach (Part sink_part in north_parts) {
                    foreach (PartResource resource in sink_part.Resources) {
                        if (resource.resourceName != "ElectricCharge" && resource.amount < resource.maxAmount && source_resources.Keys.Contains(resource.resourceName)) {
                            required_resource_types.AddUnique(resource.resourceName);
                        }
                    }
                }


                foreach(string res_name in required_resource_types){
                    Dictionary<string, int> active_tanks = new Dictionary<string, int>(){    {"available", 0},   {"required", 0} }; //count of the number of resource tanks engaged in transfer
                    Dictionary<string, double> volumes   = new Dictionary<string, double>(){ {"available", 0.0}, {"required", 0.0}, {"rate", 0.0} }; //holds the total available vs total required
                    //amount of current resource.  Also holds the max rate value as the min of all these values is used to define the amount to be transfered in this cycle.

                    //count the number of available/required tanks and resource totals.
                    foreach(PartResource res in source_resources[res_name]){
                        if(res.amount > 0){
                            active_tanks["available"] += 1;
                            volumes["available"] += res.amount;
                        }
                    }    
                    foreach(PartResource res in sink_resources[res_name]){
                        if(res.maxAmount - res.amount > 0){
                            active_tanks["required"] += 1;
                            volumes["required"] += (res.maxAmount - res.amount);
                        }
                    }    

                    //calculate the rate at which to transfer this resouce from each tank, based on how many tanks are active in transfer, size of docking port and time warp
                    volumes["rate"] = (flow_rate * 500) / active_tanks.Values.Min();//rate is set as the flow_rate divided by the smallest number of active tanks.
                    volumes["rate"] = volumes["rate"] * this.part.aerodynamicArea;  //factor size of docking port in rate of flow (larger docking ports have high flow rate).
                    volumes["rate"] = volumes["rate"] * TimeWarp.deltaTime;         //factor in physics warp

                    double to_transfer = volumes.Values.Min();  //the amount to transfer is selected as the min of either the required or available 
                    //resources or the rate (which acts as a max flow value).

                    //transfer resources between source and sink tanks.
                    int i = sink_resources[res_name].Count;
                    foreach (PartResource res in sink_resources[res_name]) {
                        //calcualte how much to transfer into a tank
                        double max_t = new double[]{ to_transfer/i, (res.maxAmount - res.amount) }.Min(); //calc the max to be transfered into this tank
                        //either as amount remaining to transfer divided by number of tanks remaining to transfer into OR free space in tank, whichever is smaller
                        res.amount += max_t;            //add the amount to the tank
                        to_transfer -= max_t;           //and deduct it from remaining amount to transfer
                        resources_transfered += max_t;  //add amount added to overall track of transfered resources
                        i -= 1;                         //reduce count of remaining tanks to transfer to

                        //extract the amount added to tank from source tanks.
                        int j = source_resources[res_name].Count;
                        foreach (PartResource s_res in source_resources[res_name]) {
                            double max_e = new double[]{ max_t/j, s_res.amount }.Min(); //calc the max to extract as either the total amount added 
                            //(max_t) over number of source tanks OR take the available anount in the tank, which ever is smaller
                            s_res.amount -= max_e;  //deduct amonut from tank
                            max_t -= max_e;         //and deduct it from the amount remaining to extract
                            j -= 1;                 //reduce the count of remaining tanks to extract from
                        }
                        //handle rounding errors - res.amount is a double, so division of doubles can result in rounding errors.  The descrepancy is 
                        //the amount remaining on max_t, so the descrepency is deducted from the sink tank (and from the total overall transfer).
                        res.amount -= max_t;
                        resources_transfered -= max_t;
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
//                log("transfered: " + resources_transfered);
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


    //Alterations to base DockingFuelPump class to enable it to work with the Claw
    public class ClawFuelPump : DockingFuelPump
    {

        [KSPEvent(guiActive = true, guiName = "Extract Fuel")]
        public void pump_in(){
            reverse_pump = true;
            start_fuel_pump();
        }

        public override void stop_fuel_pump(){
            Events["pump_in"].active = true;
            base.stop_fuel_pump();
        }

        public override void start_fuel_pump(){
            base.start_fuel_pump();
            if (is_docked) {
                Events["pump_in"].active = false;
            }
        }

        public override void get_docked_info(){
            docked_to = find_attached_part();
            if(docked_to){
                is_docked = true;
            }
        }

        public override void get_part_groups(){
            if (reverse_pump) {
                south_parts = get_descendant_parts(docked_to);
                north_parts = get_descendant_parts(this.part);
            } else {
                south_parts = get_descendant_parts(this.part);
                north_parts = get_descendant_parts(docked_to);
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


//            this.part.Highlight(Color.red);
//            this.part.parent.Highlight(Color.blue);
//            foreach(Part part in this.part.children){
//                part.Highlight(Color.green);
//            }

            ModuleDockingNode module = this.part.FindModuleImplementing<ModuleDockingNode>();
            log(module.state);
//            module.state = ModuleDockingNode.StartState.None;
            module.otherNode = null;
            log(module.state);

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