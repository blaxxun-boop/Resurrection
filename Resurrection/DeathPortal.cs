using UnityEngine;
using System.Collections;

namespace Resurrection;

public class DeathPortal : MonoBehaviour
{
	public GameObject startSmokeFX = null!;
	public GameObject portalFX = null!;
	public GameObject innerFX = null!;
	public ParticleSystem sparkParticles = null!;
	public ParticleSystem smokeParticles = null!;
	public ParticleSystem fireParticles = null!;
	public AudioSource portalAudio = null!;

	private void Start()
	{
		portalFX.SetActive(false);
		startSmokeFX.SetActive(false);
		fireParticles.Stop();

		StartCoroutine(nameof(OpenPortal));
	}

	private IEnumerator OpenPortal()
	{
		portalAudio.gameObject.SetActive(true);
		portalAudio.Play();
		startSmokeFX.SetActive(true);
		yield return new WaitForSeconds(1.17f);

		portalFX.SetActive(true);
		innerFX.SetActive(true);
		yield return new WaitForSeconds(3.17f);
		fireParticles.Play();

		yield return new WaitForSeconds(2.9f);
		sparkParticles.Stop();
		fireParticles.Stop();

		yield return new WaitForSeconds(2.0f);
		smokeParticles.Stop();
		innerFX.SetActive(false);
		portalFX.SetActive(false);
		startSmokeFX.SetActive(false);
	}
}
