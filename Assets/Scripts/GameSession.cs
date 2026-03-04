using UnityEngine;
using UnityEngine.Serialization;

public class GameSession : MonoBehaviour
{
    public static GameSession Instance;
    [FormerlySerializedAs("BoutLength")] public int boutLength =5;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }
}
