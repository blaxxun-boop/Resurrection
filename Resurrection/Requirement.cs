using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using JetBrains.Annotations;
using UnityEngine;

namespace Resurrection;

[PublicAPI]
public struct Requirement
{
	public string itemName;
	public int amount;

	public ItemDrop? GetItem()
	{
		ItemDrop? item = ObjectDB.instance.GetItemPrefab(itemName)?.GetComponent<ItemDrop>();
		if (item == null && itemName != "")
		{
			Debug.LogWarning($"The required item '{itemName}' does not exist.");
		}
		return item;
	}
}

[PublicAPI]
public class SerializedRequirements
{
	public static void drawConfigTable(ConfigEntryBase cfg)
	{
		bool locked = cfg.Description.Tags.Select(a => a.GetType().Name == "ConfigurationManagerAttributes" ? (bool?)a.GetType().GetField("ReadOnly")?.GetValue(a) : null).FirstOrDefault(v => v != null) ?? false;

		List<Requirement> newReqs = new();
		bool wasUpdated = false;

		GUILayout.BeginVertical();
		foreach (Requirement req in new SerializedRequirements((string)cfg.BoxedValue).Reqs)
		{
			GUILayout.BeginHorizontal();

			int amount = req.amount;
			if (int.TryParse(GUILayout.TextField(amount.ToString(), new GUIStyle(GUI.skin.textField) { fixedWidth = 40 }), out int newAmount) && newAmount != amount && !locked)
			{
				amount = newAmount;
				wasUpdated = true;
			}

			string newItemName = GUILayout.TextField(req.itemName);
			string itemName = locked ? req.itemName : newItemName;
			wasUpdated = wasUpdated || itemName != req.itemName;

			if (GUILayout.Button("x", new GUIStyle(GUI.skin.button) { fixedWidth = 21 }) && !locked)
			{
				wasUpdated = true;
			}
			else
			{
				newReqs.Add(new Requirement { amount = amount, itemName = itemName });
			}

			if (GUILayout.Button("+", new GUIStyle(GUI.skin.button) { fixedWidth = 21 }) && !locked)
			{
				wasUpdated = true;
				newReqs.Add(new Requirement { amount = 1, itemName = "" });
			}

			GUILayout.EndHorizontal();
		}
		GUILayout.EndVertical();

		if (wasUpdated)
		{
			cfg.BoxedValue = new SerializedRequirements(newReqs).ToString();
		}
	}

	public readonly List<Requirement> Reqs;

	public SerializedRequirements(List<Requirement> reqs) => Reqs = reqs;

	public SerializedRequirements(string reqs)
	{
		Reqs = reqs.Split(',').Select(r =>
		{
			string[] parts = r.Split(':');
			return new Requirement { itemName = parts[0], amount = parts.Length > 1 && int.TryParse(parts[1], out int amount) ? amount : 1 };
		}).ToList();
	}

	public override string ToString()
	{
		return string.Join(",", Reqs.Select(r => $"{r.itemName}:{r.amount}"));
	}
}
