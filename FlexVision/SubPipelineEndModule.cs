using System;
using System.ComponentModel;

namespace FlexVision
{
    [Serializable]
    public class SubPipelineEndModule : BaseInspectionModule
    {
        [Browsable(false)] public override string PureName => "End Sub-Pipeline Loop";
        [Browsable(false)] public override VisionModuleType ModuleType => VisionModuleType.OtsuFusion; // Vagy új enum érték

        public SubPipelineEndModule()
        {
            // Mivel ez egy strukturális lezárás, elrejtjük a felesleges bemeneti láncolásokat a PropertyGridről
            this.InputStepLink = "N/A";
        }

        public override void Process(InspectionContext context)
        {
            // A lezáró modul önmagában nem módosít képet, csak egy logikai zászló (flag) az engine számára
            this.Status = ModuleStatus.Success;
        }
    }
}
