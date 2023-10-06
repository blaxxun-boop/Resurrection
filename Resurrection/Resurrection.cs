using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using JetBrains.Annotations;
using ServerSync;
using UnityEngine;
using UnityEngine.UI;
using LocalizationManager;

namespace Resurrection;

[BepInPlugin(ModGUID, ModName, ModVersion)]
[BepInIncompatibility("org.bepinex.plugins.valheim_plus")]
[BepInDependency("org.bepinex.plugins.groups", BepInDependency.DependencyFlags.SoftDependency)]
[BepInDependency("aedenthorn.InstantMonsterDrop", BepInDependency.DependencyFlags.SoftDependency)]
public class Resurrection : BaseUnityPlugin
{
	private const string ModName = "Resurrection";
	private const string ModVersion = "1.0.8";
	private const string ModGUID = "org.bepinex.plugins.resurrection";

	private static readonly ConfigSync configSync = new(ModName) { DisplayName = ModName, CurrentVersion = ModVersion, MinimumRequiredVersion = ModVersion };

	private static ConfigEntry<Toggle> serverConfigLocked = null!;
	private static ConfigEntry<int> respawnHealth = null!;
	public static ConfigEntry<string> resurrectCosts = null!;
	public static ConfigEntry<float> resurrectionTime = null!;
	public static ConfigEntry<Toggle> groupResurrection = null!;

	private class ConfigurationManagerAttributes
	{
		[UsedImplicitly] public Action<ConfigEntryBase>? CustomDrawer;
	}

	private ConfigEntry<T> config<T>(string group, string name, T value, ConfigDescription description, bool synchronizedSetting = true)
	{
		ConfigEntry<T> configEntry = Config.Bind(group, name, value, description);

		SyncedConfigEntry<T> syncedConfigEntry = configSync.AddConfigEntry(configEntry);
		syncedConfigEntry.SynchronizedConfig = synchronizedSetting;

		return configEntry;
	}

	private ConfigEntry<T> config<T>(string group, string name, T value, string description, bool synchronizedSetting = true) => config(group, name, value, new ConfigDescription(description), synchronizedSetting);

	public enum Toggle
	{
		On = 1,
		Off = 0,
	}

	private static GameObject? respawnDialog;
	private static GameObject deathportalfab = null!;
	public static float resurrectionEndTime;
	public static string resurrectionTarget = null!;
	private static Resurrection self = null!;

	public void Awake()
	{
		Localizer.Load();

		self = this;

		serverConfigLocked = config("1 - General", "Lock Configuration", Toggle.On, "If on, the configuration is locked and can be changed by server admins only.");
		configSync.AddLockingConfigEntry(serverConfigLocked);
		respawnHealth = config("1 - General", "Respawn Health", 25, new ConfigDescription("Percentage of health after being resurrected.", new AcceptableValueRange<int>(1, 100)));
		resurrectCosts = config("1 - General", "Resurrection Cost", "SurtlingCore:1", new ConfigDescription("Items required to resurrect someone.", null, new ConfigurationManagerAttributes { CustomDrawer = SerializedRequirements.drawConfigTable }));
		resurrectionTime = config("1 - General", "Resurrection Time", 5f, new ConfigDescription("Time in seconds required to resurrect someone.", new AcceptableValueRange<float>(0f, 10f)));
		groupResurrection = config("1 - General", "Group Resurrection", Toggle.On, new ConfigDescription("If on, only other group members may resurrect a player. Requires the group mod to have any effect."));

		Assembly assembly = Assembly.GetExecutingAssembly();
		Harmony harmony = new(ModGUID);
		harmony.PatchAll(assembly);

		LoadAssets();
	}

	[HarmonyPatch]
	private static class FixInstantMonsterDrop
	{
		private static MethodInfo TargetMethod()
		{
			if (Type.GetType("InstantMonsterDrop.BepInExPlugin+Ragdoll_Awake_Patch, InstantMonsterDrop") is { } instantMonsterDrop)
			{
				return AccessTools.DeclaredMethod(instantMonsterDrop, "Postfix");
			}
			return AccessTools.DeclaredMethod(typeof(FixInstantMonsterDrop), nameof(Prefix));
		}

		private static bool Prefix(Ragdoll __0) => !__0.name.StartsWith("Player_ragdoll");
	}

	[HarmonyPatch(typeof(Player), nameof(Player.Awake))]
	private static class AddRPCs
	{
		private static void Postfix(Player __instance)
		{
			__instance.m_nview.Register("Resurrection Resurrected", _ => onResurrected(__instance));
			__instance.m_nview.Register("Resurrection Start", _ => onResurrectStart());
		}
	}

	private static void onResurrectStart()
	{
		Ragdoll ragdoll = Player.m_localPlayer.m_ragdoll;
		ragdoll.m_nview.GetZDO().Set("Resurrection PlayerInfo Started", true);

		self.Invoke(nameof(SendResurrectedRPC), 4f);
		respawnDialog?.SetActive(false);

		Vector3 targetPos = ragdoll.transform.position + Vector3.up;
		Instantiate(deathportalfab, targetPos, Quaternion.Euler(0, 0, 90));
	}

	public void SendResurrectedRPC()
	{
		if (Player.m_localPlayer is { } player)
		{
			player.m_nview.GetZDO().Set("dead", false);
			player.SetHealth(player.GetMaxHealth() * respawnHealth.Value / 100);
			ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, player.GetZDOID(), "Resurrection Resurrected");
		}
	}

	private static void onResurrected(Player player)
	{
		player.m_visual.SetActive(true);
		player.m_body.isKinematic = false;
		player.m_body.detectCollisions = true;
		if (player.GetRagdoll())
		{
			ZNetScene.instance.Destroy(player.GetRagdoll().gameObject);
		}
	}

	[HarmonyPatch(typeof(Player), nameof(Player.RPC_OnDeath))]
	private static class DisablePlayerMoving
	{
		private static void Postfix(Player __instance)
		{
			__instance.m_body.isKinematic = true;
			__instance.m_body.detectCollisions = false;
		}
	}

	[HarmonyPatch(typeof(FootStep), nameof(FootStep.Start))]
	private static class HideDeadPlayer
	{
		private static void Postfix(FootStep __instance)
		{
			if (__instance.GetComponent<Player>() is { } player && player.m_nview.GetZDO()?.GetBool("dead") == true)
			{
				player.m_visual.SetActive(false);
				player.m_body.isKinematic = true;
				player.m_body.detectCollisions = false;
			}
		}
	}

	[HarmonyPatch(typeof(Humanoid), nameof(Humanoid.OnRagdollCreated))]
	private static class AddInteract
	{
		private static void Postfix(Humanoid __instance, Ragdoll ragdoll)
		{
			if (__instance is Player player && player == Player.m_localPlayer && !player.GetComponent<ResInteract>())
			{
				resurrectionEndTime = 0;

				respawnDialog?.SetActive(true);

				ragdoll.m_nview.GetZDO().Set("Resurrection PlayerInfo PlayerName", Player.m_localPlayer.GetHoverName());
				ragdoll.m_nview.GetZDO().Set("Resurrection PlayerInfo PlayerId", Player.m_localPlayer.GetZDOID());
				ragdoll.gameObject.AddComponent<ResInteract>();
				ragdoll.CancelInvoke(nameof(Ragdoll.DestroyNow));
			}
		}
	}

	[HarmonyPatch(typeof(Ragdoll), nameof(Ragdoll.Awake))]
	private static class SyncInteract
	{
		private static void Postfix(Ragdoll __instance)
		{
			if (__instance.m_nview.GetZDO().GetString("Resurrection PlayerInfo PlayerName") != "")
			{
				Transform body = __instance.transform.Find("player_male/body");
				body.gameObject.layer = LayerMask.NameToLayer("Default");
				body.gameObject.AddComponent<ResInteract>();
				MeshCollider collider = body.gameObject.AddComponent<MeshCollider>();
				collider.sharedMesh = body.GetComponent<SkinnedMeshRenderer>().sharedMesh;
				Rigidbody rigidbody = body.gameObject.AddComponent<Rigidbody>();
				rigidbody.mass = 50;
				rigidbody.isKinematic = true;
			}
		}
	}

	[HarmonyPatch(typeof(Game), nameof(Game.RequestRespawn))]
	private static class DelayRespawn
	{
		private static bool Prefix()
		{
			return !Player.m_localPlayer || !Player.m_localPlayer.m_nview.GetZDO().GetBool("dead");
		}
	}

	[HarmonyPatch(typeof(Hud), nameof(Hud.UpdateBlackScreen))]
	private class PreventScreenBlackout
	{
		private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
		{
			MethodInfo isDead = AccessTools.DeclaredMethod(typeof(Character), nameof(Character.IsDead));
			foreach (CodeInstruction instruction in instructions)
			{
				if (instruction.OperandIs(isDead))
				{
					yield return new CodeInstruction(OpCodes.Pop);
					yield return new CodeInstruction(OpCodes.Ldc_I4_0);
				}
				else
				{
					yield return instruction;
				}
			}
		}
	}

	[HarmonyPatch(typeof(Menu), nameof(Menu.Start))]
	private class AddRespawnDialog
	{
		private static void Postfix()
		{
			respawnDialog = Instantiate(Menu.instance.m_quitDialog.gameObject, Hud.instance.m_rootObject.transform.parent.parent, true);
			respawnDialog.transform.Find("dialog/Exit").GetComponent<Text>().text = Localization.instance.Localize("$resurrection_you_died_message");
			Button.ButtonClickedEvent respawnClicked = new();
			respawnClicked.AddListener(onRespawnClicked);
			respawnDialog.transform.Find("dialog/Button_no").GetComponent<Button>().transform.localPosition = new Vector3(0, respawnDialog.transform.Find("dialog/Button_no").GetComponent<Button>().transform.localPosition.y);
			respawnDialog.transform.Find("dialog/Button_no").GetComponent<Button>().onClick = respawnClicked;
			respawnDialog.transform.Find("dialog/Button_no/Text").GetComponent<Text>().text = Localization.instance.Localize("$resurrection_respawn");
			Destroy(respawnDialog.transform.Find("dialog/Button_yes").gameObject);
		}
	}

	private static void onRespawnClicked()
	{
		Player.m_localPlayer.GetRagdoll().DestroyNow();
		Game.instance._RequestRespawn();
		respawnDialog?.SetActive(false);
	}

	[HarmonyPatch(typeof(TextInput), nameof(TextInput.IsVisible))]
	private class DisablePlayerInputInRespawnDialog
	{
		private static void Postfix(ref bool __result)
		{
			if (respawnDialog && respawnDialog?.activeSelf == true)
			{
				__result = true;
			}
		}
	}

	[HarmonyPatch(typeof(ZNetScene), nameof(ZNetScene.Awake))]
	private class AddDeathportalToZnetScene
	{
		private static void Prefix(ZNetScene __instance)
		{
			__instance.m_prefabs.Add(deathportalfab);
		}
	}

	private static AssetBundle LoadAssetBundle(string bundleName)
	{
		string resource = typeof(Resurrection).Assembly.GetManifestResourceNames().Single(s => s.EndsWith(bundleName));
		return AssetBundle.LoadFromStream(typeof(Resurrection).Assembly.GetManifestResourceStream(resource));
	}

	private static void LoadAssets()
	{
		AssetBundle assets = LoadAssetBundle("deathportal");
		deathportalfab = assets.LoadAsset<GameObject>("DeathPortal");
	}

	[HarmonyPatch(typeof(Player), nameof(Player.GetActionProgress))]
	private class AddCraftingAnimation
	{
		private static void Postfix(ref string name, ref float progress)
		{
			if (resurrectionEndTime > 0)
			{
				progress = 1 - (resurrectionEndTime - Time.fixedTime) / resurrectionTime.Value;
				name = Localization.instance.Localize("$resurrection_resurrecting_message", resurrectionTarget);
			}
		}
	}

	[HarmonyPatch(typeof(Player), nameof(Player.ClearActionQueue))]
	private class CancelResurrection
	{
		private static void Postfix()
		{
			resurrectionEndTime = 0;
		}
	}

	[HarmonyPatch(typeof(Character), nameof(Character.RPC_Damage))]
	private class InterruptResurrection
	{
		private static void Postfix(Character __instance, HitData hit)
		{
			if (__instance == Player.m_localPlayer && hit.HaveAttacker())
			{
				if (resurrectionEndTime > 0)
				{
					Player.m_localPlayer.Message(MessageHud.MessageType.Center, Localization.instance.Localize("$resurrection_interrupted_message"));
					resurrectionEndTime = 0;
				}
			}
		}
	}
}
