using System.Threading.Tasks;

namespace Eco.Mods.Companies
{
    using Shared.Localization;

    using Gameplay.Players;
    using Gameplay.Systems.TextLinks;
    using Gameplay.Systems.Messaging.Chat.Commands;
    using Eco.Gameplay.Settlements;
    using Eco.Core.Controller;

    [ChatCommandHandler]
    public static class CompanyCommands
    {
        [ChatCommand("Company", ChatAuthorizationLevel.User)]
        public static void Company() { }

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
            var confirmed = await user.Player.ConfirmBoxLoc($"{createAttempt.ToLocString()}\nThis action is irreversible.\nDo you wish to proceed?");
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