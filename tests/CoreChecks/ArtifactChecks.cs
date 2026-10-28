using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

static class ArtifactChecks
{
    internal static void Run(string plugin,string native,Action<bool,string> check)
    {
        using var nativeStream=File.OpenRead(native);using var nativePe=new PEReader(nativeStream);var game=nativePe.GetMetadataReader();
        void Method(string type,string method,int arguments)
        {
            var definition=game.TypeDefinitions.Select(game.GetTypeDefinition).Single(t=>game.GetString(t.Name)==type);
            var count=definition.GetMethods().Select(game.GetMethodDefinition).Count(m=>game.GetString(m.Name)==method&&m.GetParameters().Count(h=>game.GetParameter(h).SequenceNumber>0)==arguments);
            check(count==1,$"Exact declared native target exists: {type}.{method}/{arguments}");
        }
        Method("RunDataManager","StartNewRunRpc",5);Method("Player","OnDamageAccounted",1);Method("Player","OnHealthChanged",2);Method("GameLevel","OnLevelCompleteForPeer",0);
        foreach(var type in new[]{"UIGameLobbyTrainerRpc","UIGameLobbyDoctorRpc","UIGameLobbyShopRpc"})
        {foreach(var method in new[]{"HandleSubmit","HandleCancel","HandleSubmitRpc","HandleCancelRpc"})Method(type,method,1);Method(type,"HandleNavigation",2);Method(type,"HandleNavigationRpc",2);}
        Method("UIGameLobbyTrainerRpc","HandleUpgradeButtonSelectedRpc",2);Method("UIGameLobbyTrainerRpc","HandleSoloUpgradeButtonSelectedRpc",3);
        Method("UIGameLobbyDoctorRpc","HandleAutoSelectCompleteRpc",1);Method("UIGameLobbyDoctorRpc","TryExecuteSelections",0);
        Method("UIGameLobbyShopRpc","HandlePurchaseSelectionRpc",2);Method("UIGameLobbyShopRpc","HandlePlayerReadyRpc",1);Method("UIGameLobbyShopRpc","Execute",0);
        using var stream=File.OpenRead(plugin);using var pe=new PEReader(stream);var md=pe.GetMetadataReader();
        check(md.GetString(md.GetAssemblyDefinition().Name)=="ChainedBeasts.Rerolls","The artifact has its own plugin identity.");
        check(md.GetAssemblyDefinition().Version.ToString()=="0.1.0.0","The compiled preview version matches its own project and plugin version.");
        var refs=md.AssemblyReferences.Select(h=>md.GetString(md.GetAssemblyReference(h).Name)).ToArray();
        check(!refs.Any(n=>n.Contains("ModFoundation")||n.Contains("Matchmaking")||n.Contains("BattleNumbers")||n.Contains("TermsAndContusions")),"Standalone plugin has no hard dependency on existing mods.");
        var controller=md.TypeDefinitions.Select(md.GetTypeDefinition).Single(t=>md.GetString(t.Name)=="RerollController");
        var methods=controller.GetMethods().Select(md.GetMethodDefinition).Where(m=>(m.Attributes&MethodAttributes.Public)!=0).Select(m=>md.GetString(m.Name)).ToArray();
        check(methods.OrderBy(x=>x).SequenceEqual(new[]{".ctor","OnDestroy","OnGUI","Update"}.OrderBy(x=>x)),"Injected type exposes only supported Unity callbacks and pointer constructor.");
        var banned=new[]{"HealRpc","TakeDamage","TakeDamageRpc","ConsumePotionRpc","AddPlayerSkillRpc","ApplyAddPlayerSkill","set_health","set_money","set_Money","set_completedLevels"};
        var calls=md.MemberReferences.Select(h=>md.GetString(md.GetMemberReference(h).Name)).ToArray();
        check(!calls.Intersect(banned).Any(),"The plugin calls no health, money, skill-grant, or progression mutation path.");
        var icon=md.ManifestResources.Select(md.GetManifestResource).Single(r=>md.GetString(r.Name)=="Rerolls.Icon.alpha");
        check(icon.Implementation.IsNil,"CC0 graphic is embedded, with no external runtime download.");
        var reader=pe.GetSectionData(pe.PEHeaders.CorHeader!.ResourcesDirectory.RelativeVirtualAddress).GetReader((int)icon.Offset,4100);
        check(reader.ReadInt32()==4096&&reader.ReadBytes(4096).Count(b=>b!=0)>100,"Embedded graphic is a complete visible alpha mask.");
    }
}
