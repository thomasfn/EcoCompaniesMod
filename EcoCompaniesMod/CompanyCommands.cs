using System.Threading.Tasks;

namespace Eco.Mods.Companies
{
    using Shared.Localization;

    using Gameplay.Players;
    using Gameplay.Systems.TextLinks;
    using Gameplay.Systems.Messaging.Chat.Commands;
    using Gameplay.Civics.GameValues;
    using Gameplay.Settlements;
    using Gameplay.Items;

    [ChatCommandHandler]
    public static class CompanyCommands
    {
        [ChatCommand("Company", ChatAuthorizationLevel.User)]
        public static void Company() { }

        [ChatSubCommand("Company", "Check company mod configuration.", ChatAuthorizationLevel.User)]
        public static void Config(User user)
        {
            if (CompaniesPlugin.Obj.Config.PropertyLimitsEnabled)
            {
                user.MsgLoc($"Company property limits mode is ENABLED.");
            }
            else
            {
                user.MsgLoc($"Company property limits mode is DISABLED.");
            }
        }

        [ChatSubCommand("Company", "Found a new company.", ChatAuthorizationLevel.User)]
        public static async Task Create(User user, string name)
        {
            var createAttempt = CompanyManager.Obj.CreateNewDryRun(user, name.Trim(), out var errorMessage);
            if (!createAttempt.IsValid)
            {
                user.Player?.OkBox(new LocString(errorMessage));
                return;
            }
            if (user.Player == null)
            {
                CompanyManager.Obj.CreateNew(user, name, createAttempt, out errorMessage);
                return;
            }
            var confirmed = await user.Player.ConfirmBoxLoc($"{createAttempt.ToLocString()}\nOnce founded, a company cannot be dissolved and exists permanently.\nDo you wish to proceed?");
            if (!confirmed) { return; }
            var company = CompanyManager.Obj.CreateNew(user, name, createAttempt, out errorMessage);
            if (company == null)
            {
                user.Player?.OkBox(new LocString(errorMessage));
                return;
            }
            user.Player?.OkBoxLoc($"You have founded {company.UILink()}.");
        }

        [ChatSubCommand("Company", "Invite another player to your company.", ChatAuthorizationLevel.User)]
        public static void Invite(User user, User otherUser)
        {
            var company = Companies.Company.GetEmployer(user);
            if (company == null)
            {
                user.OkBoxLoc($"Couldn't send company invite as you are not a CEO of any company");
                return;
            }
            if (!company.TryInvite(user, otherUser, out var errorMessage))
            {
                user.OkBox(errorMessage);
            }
        }

        [ChatSubCommand("Company", "Withdraws an invitation for another player to your company.", ChatAuthorizationLevel.User)]
        public static void Uninvite(User user, User otherUser)
        {
            var company = Companies.Company.GetEmployer(user);
            if (company == null)
            {
                user.OkBoxLoc($"Couldn't withdraw company invite as you are not a CEO of any company");
                return;
            }
            if (!company.TryUninvite(user, otherUser, out var errorMessage))
            {
                user.OkBox(errorMessage);
            }
        }

        [ChatSubCommand("Company", "Removes an employee from your company.", ChatAuthorizationLevel.User)]
        public static void Fire(User user, User otherUser)
        {
            var company = Companies.Company.GetEmployer(user);
            if (company == null)
            {
                user.OkBoxLoc($"Couldn't fire employee as you are not a CEO of any company");
                return;
            }
            if (!company.TryFire(user, otherUser, out var errorMessage))
            {
                user.OkBox(errorMessage);
            }
        }

        [ChatSubCommand("Company", "Accepts an invite to join a company.", ChatAuthorizationLevel.User)]
        public static void Join(User user, Company company)
        {
            if (!company.TryJoin(user, out var errorMessage))
            {
                user.OkBox(errorMessage);
                return;
            }
        }

        [ChatSubCommand("Company", "Resigns you from your current company.", ChatAuthorizationLevel.User)]
        public static void Leave(User user)
        {
            var currentEmployer = Companies.Company.GetEmployer(user);
            if (currentEmployer == null)
            {
                user.OkBoxLoc($"Couldn't resign from your company as you're not currently employed");
                return;
            }
            if (!currentEmployer.TryLeave(user, out var errorMessage))
            {
                user.OkBox(errorMessage);
                return;
            }
        }

        [ChatSubCommand("Company", "Sets the currently held claim tool to the company HQ deed.", ChatAuthorizationLevel.User)]
        public static void Claim(User user)
        {
            var currentEmployer = Companies.Company.GetEmployer(user);
            if (currentEmployer == null)
            {
                user.OkBoxLoc($"Couldn't set claim mode as you're not currently employed");
                return;
            }
            if (currentEmployer.HQDeed == null)
            {
                user.OkBoxLoc($"Couldn't set claim mode as {currentEmployer.MarkedUpName} does not currently have a HQ");
                return;
            }
            if (user.SelectedItem is not ClaimToolBaseItem claimTool)
            {
                user.OkBoxLoc($"Couldn't set claim mode as you're not currently holding a claim tool");
                return;
            }
            claimTool.Deed = currentEmployer.HQDeed;
            typeof(ClaimToolBaseItem)
                .GetMethod("SetClaimMode", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                .Invoke(claimTool, new object[] { user.Player });
            user.MsgLoc($"Your claim tool has been set to {currentEmployer.HQDeed.UILink()}.");
        }

        [ChatSubCommand("Company", "Provides options for company citizenship.", ChatAuthorizationLevel.User)]
        public static void Citizenship(User user, string verb = "", Settlement targetSettlement = null)
        {
            var currentEmployer = Companies.Company.GetEmployer(user);
            if (currentEmployer == null) { return; }

            LocString errorMessage;
            switch (verb)
            {
                case "":
                    if (currentEmployer.DirectCitizenship != null)
                    {
                        user.MsgLoc($"{currentEmployer.UILink()} is currently a direct citizen of {currentEmployer.DirectCitizenship.UILink()}.");
                    }
                    else
                    {
                        user.MsgLoc($"{currentEmployer.UILink()} is currently not a citizen of any settlement.");
                    }
                    break;
                case "apply":
                    if (targetSettlement == null)
                    {
                        user.MsgLoc($"You must specify a valid settlement to apply to.");
                        return;
                    }
                    if (!currentEmployer.TryApplyToSettlement(user, targetSettlement, out errorMessage))
                    {
                        user.OkBox(errorMessage);
                    }
                    break;
                case "join":
                    if (targetSettlement == null)
                    {
                        user.MsgLoc($"You must specify a valid settlement to join.");
                        return;
                    }
                    if (!currentEmployer.TryJoinSettlement(user, targetSettlement, out errorMessage))
                    {
                        user.OkBox(errorMessage);
                    }
                    break;
                case "leave":
                    if (!currentEmployer.TryLeaveSettlement(user, out errorMessage))
                    {
                        user.OkBox(errorMessage);
                    }
                    break;
                case "refresh":
                    currentEmployer.UpdateCitizenships();
                    user.MsgLoc($"Citizenships refreshed.");
                    break;
                case "checkdesync":
                    if (currentEmployer.CheckCitizenshipDesync(out errorMessage))
                    {
                        user.Msg(errorMessage);
                    }
                    else
                    {
                        user.MsgLoc($"No citizenship desync detected.");
                    }
                    break;
                default:
                    user.MsgLoc($"Valid verbs are 'apply', 'join', 'leave', or blank to view citizenship status.");
                    break;
            }
        }

        [ChatSubCommand("Company", "Provides options for company HQ management.", ChatAuthorizationLevel.User)]
        public static void HQ(User user, string verb = "")
        {
            var currentEmployer = Companies.Company.GetEmployer(user);
            if (currentEmployer == null) { return; }

            switch (verb)
            {
                case "":
                    if (currentEmployer.HQDeed != null)
                    {
                        user.MsgLoc($"{currentEmployer.UILink()} currently has {currentEmployer.HQDeed.UILink()} as it's HQ.");
                    }
                    else
                    {
                        user.MsgLoc($"{currentEmployer.UILink()} currently has no HQ.");
                    }
                    break;
                case "checkhqdesync":
                    if (currentEmployer.CheckHQDesync(out var errorMessage))
                    {
                        user.Msg(errorMessage);
                    }
                    else
                    {
                        user.MsgLoc($"No HQ desync detected.");
                    }
                    break;
                case "refreshsize":
                    if (currentEmployer.HQDeed != null)
                    {
                        currentEmployer.ForceUpdateHQSize();
                        user.MsgLoc($"HQ size refreshed ({currentEmployer.HQDeed.UILinkNullSafe()} should have {currentEmployer.HQSize} base max plots)");
                    }
                    else
                    {
                        user.MsgLoc($"{currentEmployer.UILink()} currently has no HQ.");
                    }
                    break;
                default:
                    user.MsgLoc($"Valid verbs are 'checkhqdesync', 'refreshsize', or blank to view HQ status.");
                    break;
            }
        }

        /*[ChatSubCommand("Company", "Edits the company owned deed that you're currently standing in.", ChatAuthorizationLevel.User)]
        public static void EditDeed(User user)
        {
            var company = Companies.Company.GetEmployer(user);
            if (company == null)
            {
                user.Player?.OkBoxLoc($"Couldn't edit company deed as you're not currently employed");
                return;
            }
            var deed = PropertyManager.GetDeedWorldPos(new Vector2i((int)user.Position.X, (int)user.Position.Z));
            if (deed == null)
            {
                user.Player?.OkBoxLoc($"Couldn't edit company deed as you're not standing on one");
                return;
            }
            if (!company.OwnedDeeds.Contains(deed))
            {
                user.Player?.OkBoxLoc($"Couldn't edit company deed as it's not owned by {company.MarkedUpName}");
                return;
            }
            DeedEditingUtil.EditInMap(deed, user);
        }*/
    }
}