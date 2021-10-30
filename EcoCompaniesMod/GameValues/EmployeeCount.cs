using System;
using System.Linq;

namespace Eco.Mods.Companies
{
    using Core.Controller;
    using Core.Utils;
    using Core.Utils.PropertyScanning;

    using Shared.Localization;
    using Shared.Networking;
    using Shared.Utils;

    using Gameplay.Civics.GameValues;
    using Gameplay.Systems.TextLinks;
    using Gameplay.Aliases;
    using Gameplay.Players;

    [Eco, LocCategory("Companies"), LocDescription("The number of employees of a company, including the CEO.")]
    public class EmployeeCount : GameValue<float>
    {
        [Eco, Advanced, LocDescription("The legal person whose company's employee count is being evaluated.")] public GameValue<User> LegalPerson { get; set; }

        private Eval<float> FailNullSafeFloat<T>(Eval<T> eval, string paramName) =>
            eval != null ? Eval.Make($"Invalid {Localizer.DoStr(paramName)} specified on {GetType().GetLocDisplayName()}: {eval.Message}", float.MinValue)
                         : Eval.Make($"{Localizer.DoStr(paramName)} not set on {GetType().GetLocDisplayName()}.", float.MinValue);

        public override Eval<float> Value(IContextObject action)
        {
            var legalPerson = this.LegalPerson?.Value(action); if (legalPerson?.Val == null) return this.FailNullSafeFloat(legalPerson, nameof(this.LegalPerson));

            var company = Company.GetFromLegalPerson(legalPerson.Val);
            if (company == null) return this.FailNullSafeFloat(legalPerson, nameof(this.LegalPerson));
            float employeeCount = company.AllEmployees.Count();

            return Eval.Make($"{Text.StyledNum(employeeCount)} (employee count of {company.UILink()})", employeeCount);
        }

        public override LocString Description() => Localizer.Do($"employee count of company of {LegalPerson.DescribeNullSafe()}");
    }
}
