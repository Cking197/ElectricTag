using UnityEngine;

public class StripBoundary : MonoBehaviour
{
    private void OnTriggerExit2D(Collider2D other)
    {
        PlayerController player = other.GetComponent<PlayerController>();
        if (player != null)
        {
            GameManager.Instance.OnPlayerLeftStrip(player);
        }
    }
}