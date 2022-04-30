using UnityEngine;

namespace Resurrection;

public class FlickeringLight : MonoBehaviour
{
	public Light flickeringLight = null!;
	private readonly float[] smoothing = new float[20];

	private void Start()
	{
		for (int i = 0; i < smoothing.Length; i++)
		{
			smoothing[i] = .0f;
		}
	}

	private void Update()
	{
		float sum = .0f;

		for (int i = 1; i < smoothing.Length; ++i)
		{
			smoothing[i - 1] = smoothing[i];
			sum += smoothing[i - 1];
		}

		smoothing[smoothing.Length - 1] = Random.value;
		sum += smoothing[smoothing.Length - 1];

		flickeringLight.intensity = 6 * (sum / smoothing.Length);
	}
}
