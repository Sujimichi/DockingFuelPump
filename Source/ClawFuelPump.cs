using System.Collections.Generic;

namespace DockingFuelPump
{
    //Alterations to base DockingFuelPump class to enable it to work with the Claw
    public class ClawFuelPump : DockingFuelPump
    {


        [KSPEvent(guiActive = true, guiName = "Pump Fuel (claw)", active = true)]
        public void claw_pump_out(){
            reverse_pump = false;
            start_fuel_pump();
        }

        [KSPEvent(guiActive = true, guiName = "Extract Fuel", active = true)]
        public void claw_pump_in(){
            reverse_pump = true;
            start_fuel_pump();
        }


        public override void start_fuel_pump(){
            base.start_fuel_pump();

            if (is_docked) {
                log("Starting Fuel Pump on Claw");
                Events["claw_pump_out"].active = false;
                Events["claw_pump_in"].active = false;
            }
        }
        
        public override void stop_fuel_pump(){
            Events["claw_pump_out"].active = true;
            Events["claw_pump_in"].active = true;
            base.stop_fuel_pump();
        }

        internal override void check_state(bool force_check = false){
            Events["claw_pump_in"].active = true;
        }

        public override void get_docked_info(){
            docked_to = find_attached_part();
            if(docked_to){
                is_docked = true;
            }
        }

        public override void get_part_groups(){
            if (reverse_pump) {
                north_parts = get_descendant_parts(this.part);
                south_parts.Clear();
                foreach(Part part in this.vessel.parts){
                    if(!north_parts.Contains(part) && part.Resources.Count > 0){
                        south_parts.Add(part);
                    }
                }
            } else {
                south_parts = get_descendant_parts(this.part);
                north_parts.Clear();
                foreach(Part part in this.vessel.parts){
                    if(!south_parts.Contains(part) && part.Resources.Count > 0){
                        north_parts.Add(part);
                    }
                }
            }
        
        }

        public override void remove_docked_part_from(List<Part> list, Part focal_part){
            list.Remove(docked_to);
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
}

