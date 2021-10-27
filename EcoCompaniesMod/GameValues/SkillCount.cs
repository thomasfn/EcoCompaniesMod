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
    using Gameplay.Players;

    [Eco, LocCategory("Companies"), LocDescription("The number of skills held by employees of a company.")]
    public class SkillCount : GameValue<float>
    {
        [Eco, Advanced, LocDescription("The legal person whose company's skill count is being evaluated.")] public GameValue<User> LegalPerson { get; set; }

        [Eco, Advanced, LocDescription("Whether to only consider unique skills.")] public GameValue<bool> UniqueSkills { get; set; } = new No();

        private Eval<float> FailNullSafeFloat<T>(Eval<T> eval, string paramName) =>
            eval != null ? Eval.Make($"Invalid {Localizer.DoStr(paramName)} specified on {GetType().GetLocDisplayName()}: {eval.Message}", float.MinValue)
                         : Eval.Make($"{Localizer.DoStr(paramName)} not set on {GetType().GetLocDisplayName()}.", float.MinValue);

        public override Eval<float> Value(IContextObject action)
        {
            var legalPerson = this.LegalPerson?.Value(action); if (legalPerson?.Val == null) return this.FailNullSafeFloat(legalPerson, nameof(this.LegalPerson));
            var uniqueSkills = this.UniqueSkills?.Value(action); if (uniqueSkills?.Val == null) return this.FailNullSafeFloat(uniqueSkills, nameof(this.UniqueSkills));

            var company = Company.GetFromLegalPerson(legalPerson.Val);
            if (company == null) return this.FailNullSafeFloat(legalPerson, nameof(this.LegalPerson));
            var companySkills = company.AllEmployees
                .SelectMany(user => user.Skillset.Skills)
                .Where(skill => skill.Level > 0 && skill.RootSkillTree != skill.SkillTree)
                .Select(skill => skill.GetType());
            if (uniqueSkills.Val)
            {
                companySkills = companySkills.Distinct();
            }
            float companySkillCount = companySkills.Count();

            return Eval.Make($"{Text.StyledNum(companySkillCount)} ({(uniqueSkills.Val ? "unique " : "")}skill count of {company.UILink()})", companySkillCount);
        }

        public override LocString Description() => Localizer.Do($"({(UniqueSkills is Yes ? "unique " : UniqueSkills is No ? "" : $"unique (when {UniqueSkills.DescribeNullSafe()}) ")}skill count of company of {LegalPerson.DescribeNullSafe()}");
    }
}
