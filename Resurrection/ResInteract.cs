using UnityEngine;

namespace Resurrection;

public class ResInteract : MonoBehaviour, Interactable, Hoverable
{
	private bool resurrectionStarted = false;

	public bool Interact(Humanoid user, bool hold, bool alt)
	{
		if (hold)
		{
			return false;
		}

		ZDO zdo = GetComponentInParent<ZNetView>().GetZDO();
		if (zdo.GetBool("Resurrection PlayerInfo Started"))
		{
			return false;
		}

		foreach (Requirement requirement in new SerializedRequirements(Resurrection.resurrectCosts.Value).Reqs)
		{
			if (requirement.GetItem() is { } item && Player.m_localPlayer.m_inventory.CountItems(item.m_itemData.m_shared.m_name) < requirement.amount)
			{
				Player.m_localPlayer.Message(MessageHud.MessageType.Center, "You do not have the required items.");

				return false;
			}
		}

		CancelInvoke(nameof(Resurrect));
		Resurrection.resurrectionEndTime = Time.fixedTime + Resurrection.resurrectionTime.Value;
		if (Resurrection.resurrectionTime.Value > 0)
		{
			Resurrection.resurrectionTarget = zdo.GetString("Resurrection PlayerInfo PlayerName");
			Invoke(nameof(Resurrect), Resurrection.resurrectionTime.Value);
		}
		else
		{
			Resurrect();
		}

		return true;
	}

	public void Resurrect()
	{
		if (Resurrection.resurrectionEndTime > 0)
		{
			Resurrection.resurrectionEndTime = 0;

			ZDO zdo = GetComponentInParent<ZNetView>().GetZDO();
			if (zdo.GetBool("Resurrection PlayerInfo Started") || resurrectionStarted)
			{
				return;
			}

			Transform corpsePosition = transform;
			if (Utils.DistanceXZ(corpsePosition.position, Player.m_localPlayer.transform.position) > 4f)
			{
				Player.m_localPlayer.Message(MessageHud.MessageType.Center, "You are too far away to complete the resurrection.");

				return;
			}

			foreach (Requirement requirement in new SerializedRequirements(Resurrection.resurrectCosts.Value).Reqs)
			{
				if (requirement.GetItem() is { } item && Player.m_localPlayer.m_inventory.CountItems(item.m_itemData.m_shared.m_name) < requirement.amount)
				{
					Player.m_localPlayer.Message(MessageHud.MessageType.Center, "You do not have the required items.");

					return;
				}
			}

			foreach (Requirement requirement in new SerializedRequirements(Resurrection.resurrectCosts.Value).Reqs)
			{
				if (requirement.GetItem() is { } item)
				{
					Player.m_localPlayer.m_inventory.RemoveItem(item.m_itemData.m_shared.m_name, requirement.amount);
				}
			}

			resurrectionStarted = true;
			ZDOID playerId = GetComponentInParent<ZNetView>().GetZDO().GetZDOID("Resurrection PlayerInfo PlayerId");
			ZRoutedRpc.instance.InvokeRoutedRPC(playerId.m_userID, playerId, "Resurrection Start");
		}
	}

	public bool UseItem(Humanoid user, ItemDrop.ItemData item)
	{
		return false;
	}

	public string GetHoverText()
	{
		if (GetComponentInParent<ZNetView>()?.GetZDO() is { } zdo)
		{
			return zdo.GetString("Resurrection PlayerInfo PlayerName") + (zdo.GetBool("Resurrection PlayerInfo Started") ? "" : Localization.instance.Localize("\n[<color=yellow><b>$KEY_Use</b></color>] Resurrect"));
		}
		return "";
	}

	public string GetHoverName()
	{
		return GetComponentInParent<ZNetView>()?.GetZDO()?.GetString("Resurrection PlayerInfo PlayerName") ?? "";
	}
}
