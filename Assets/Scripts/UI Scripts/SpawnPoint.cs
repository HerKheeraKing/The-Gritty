//Worked on By : Joshua Furber
using Photon.Pun;
using UnityEngine;

public class SpawnPoint : MonoBehaviourPunCallbacks
{
    public static SpawnPoint Instance { get; private set; }
    public GameObject player;

    void Start() {
        Instance = this;

        if (PhotonNetwork.InRoom && player != null)
            PhotonNetwork.Instantiate("Player/" + player.name, GetRandomSpawn(), Quaternion.identity);
        else if (!PhotonNetwork.InRoom && player != null)
            Instantiate(player, GetRandomSpawn(), Quaternion.identity);
            GameManager.playerLocation = GetRandomSpawn();
    }

    Vector3 GetRandomSpawn() { return gameObject.transform.position; }
}