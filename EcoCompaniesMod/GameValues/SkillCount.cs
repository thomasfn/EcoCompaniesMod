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

        [Eco, Advanced, LocDescription("Whether to return the highest employee skill count instead of the sum.")] public GameValue<bool> Highest { get; set; } = new No();

        private Eval<float> FailNullSafeFloat<T>(Eval<T> eval, string paramName) =>
            eval != null ? Eval.Make($"Invalid {Localizer.DoStr(paramName)} specified on {GetType().GetLocDisplayName()}: {eval.Message}", float.MinValue)
                         : Eval.Make($"{Localizer.DoStr(paramName)} not set on {GetType().GetLocDisplayName()}.", float.MinValue);

        public override Eval<float> Value(IContextObject action)
        {
            var legalPerson = this.LegalPerson?.Value(action); if (legalPerson?.Val == null) return this.FailNullSafeFloat(legalPerson, nameof(this.LegalPerson));
            var uniqueSkills = this.UniqueSkills?.Value(action); if (uniqueSkills?.Val == null) return this.FailNullSafeFloat(uniqueSkills, nameof(this.UniqueSkills));
            var highest = this.Highest?.Value(action); if (highest?.Val == null) return this.FailNullSafeFloat(highest, nameof(this.Highest));

            var company = Company.GetFromLegalPerson(legalPerson.Val);
            if (company == null) return this.FailNullSafeFloat(legalPerson, nameof(this.LegalPerson));

            float companySkillCount;
            if (highest.Val)
            {
                companySkillCount = company.AllEmployees
                    .Select(user => user.Skillset.Skills.Where(skill => skill.Level > 0 && skill.RootSkillTree != skill.SkillTree).Count())
                    .OrderByDescending(count => count)
                    .FirstOrDefault();
                // TODO: Figure out what it means when unique = true, e.g. in what case does unique highest != highest?
            }
            else
            {
                var companySkills = company.AllEmployees
                    .SelectMany(user => user.Skillset.Skills)
                    .Where(skill => skill.Level > 0 && skill.RootSkillTree != skill.SkillTree)
                    .Select(skill => skill.GetType());
                if (uniqueSkills.Val)
                {
                    companySkills = companySkills.Distinct();
                }
                companySkillCount = companySkills.Count();
            }

            return Eval.Make($"{Text.StyledNum(companySkillCount)} ({(highest.Val ? "highest " : "")}{(uniqueSkills.Val ? "unique " : "")}skill count of {company.UILink()})", companySkillCount);
        }

        public override LocString Description() => Localizer.Do($"({DescribeHighest()}{DescribeUniqueSkills()}skill count of company of {LegalPerson.DescribeNullSafe()}");

        private string DescribeUniqueSkills()
            => UniqueSkills is Yes ? "unique " : UniqueSkills is No ? "" : $"unique (when {UniqueSkills.DescribeNullSafe()}) ";

        private string DescribeHighest()
            => Highest is Yes ? "highest " : Highest is No ? "" : $"highest (when {Highest.DescribeNullSafe()}) ";
    }
}
