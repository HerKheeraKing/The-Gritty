using Photon.Pun;
using System.Collections;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.AI;

public class WizardAI : MonoBehaviourPun, IDamage, IPunObservable {
    [SerializeField] Renderer model;
    [SerializeField] Material mat;
    [SerializeField] NavMeshAgent agent;
    [SerializeField] Animator anim;
    [SerializeField] float hp;
    [SerializeField] int animationTransitionSpeed, faceTargetSpeed, attackSpeed, viewAngle, shootAngle;
    [SerializeField] float iceBallTimer;
    [SerializeField] Transform headPosition;
    [SerializeField] float swingRadius;
    [SerializeField] GameObject[] meleeWeapons;
    [SerializeField] GameObject projectile, shootPos;
    [SerializeField] float meleeDamage;
    [SerializeField] EnemyLimiter enemyLimiter;
    [SerializeField] int range;

    DamageStats status;
    bool isAttacking, wasKilled, isDOT, iceBallShooting;
    Vector3 playerDirection, enemyTargetPosition, netPos;
    Quaternion netRot;
    float originalStoppingDistance, adjustedStoppingDistance, angleToPlayer, curTime;
    int id;

    void Start() {
        isAttacking = wasKilled = isDOT = iceBallShooting = false;
        GameManager.instance.updateEnemy(1);
        foreach (GameObject melee in meleeWeapons)
            melee.AddComponent<WeaponController>().SetWeapon(meleeDamage, false, null);
        EnemyManager.Instance.AddEnemyType(enemyLimiter);
        originalStoppingDistance = agent.stoppingDistance;
        adjustedStoppingDistance = originalStoppingDistance * enemyLimiter.rangeMultiplier;
        id = gameObject.GetInstanceID();
        if (!photonView.IsMine && PhotonNetwork.IsMasterClient)
            photonView.TransferOwnership(PhotonNetwork.MasterClient);

    }

    void Update() {
        anim.SetFloat("Speed", Mathf.Lerp(anim.GetFloat("Speed"), agent.velocity.normalized.magnitude, Time.deltaTime * animationTransitionSpeed));

        if (CanSeePlayer() && (PhotonNetwork.IsMasterClient || !PhotonNetwork.IsConnected)) {
            if (!iceBallShooting)
                agent.SetDestination(enemyTargetPosition);

            if (agent.remainingDistance < agent.stoppingDistance)
                FaceTarget();

            if (!EnemyManager.Instance.IsClose(enemyLimiter, id)) {
                if (EnemyManager.Instance.CanBeClose(enemyLimiter) && agent.remainingDistance < range && !agent.pathPending)
                    EnemyManager.Instance.AddCloseEnemy(enemyLimiter, id);
                else if (!EnemyManager.Instance.CanBeClose(enemyLimiter))
                    agent.stoppingDistance = adjustedStoppingDistance;
            }
            else if (EnemyManager.Instance.IsClose(enemyLimiter, id) && agent.remainingDistance > range) {
                EnemyManager.Instance.RemoveCloseEnemy(enemyLimiter, id);
                agent.stoppingDistance = originalStoppingDistance;
            }

            if (!isAttacking && agent.remainingDistance < swingRadius && EnemyManager.Instance.CanAttack(enemyLimiter)) {
                if (PhotonNetwork.IsConnected)
                    photonView.RPC(nameof(StartSwing), RpcTarget.All);
                else
                    StartSwing();
            }

            if (!isAttacking && EnemyManager.Instance.CanAttack(enemyLimiter) && curTime == 0) {
                if (PhotonNetwork.IsConnected)
                    photonView.RPC(nameof(StartIceBall), RpcTarget.All);
                else
                    StartIceBall();
            }
        }

        if (!PhotonNetwork.IsMasterClient && PhotonNetwork.InRoom) {
            transform.position = Vector3.Lerp(transform.position, netPos, Time.deltaTime * 10);
            transform.rotation = Quaternion.Lerp(transform.rotation, netRot, Time.deltaTime * 10);
        }
    }

    public EnemyLimiter GetEnemyLimiter() { return enemyLimiter; }

    bool CanSeePlayer() {
        GameObject closestPlayer = FindClosestPlayer();
        if (closestPlayer == null) return false;

        playerDirection = closestPlayer.transform.position - headPosition.position;
        angleToPlayer = Vector3.Angle(new Vector3(playerDirection.x, playerDirection.y + 1, playerDirection.z), transform.forward);

        if (Physics.Raycast(headPosition.position, playerDirection, out RaycastHit hit) && hit.collider.CompareTag("Player") && angleToPlayer < viewAngle && !wasKilled) {
            enemyTargetPosition = closestPlayer.transform.position;
            return true;
        }
        return false;
    }

    GameObject FindClosestPlayer() {
        GameObject[] players = GameObject.FindGameObjectsWithTag("Player");
        GameObject closestPlayer = null;
        float closestDistance = Mathf.Infinity;

        foreach (GameObject player in players) {
            float distance = Vector3.Distance(transform.position, player.transform.position);
            if (distance < closestDistance) {
                closestDistance = distance;
                closestPlayer = player;
            }
        }

        return closestPlayer;
    }

    void FaceTarget() {
        Quaternion rotation = Quaternion.LookRotation(playerDirection);
        transform.rotation = Quaternion.Lerp(transform.rotation, rotation, Time.deltaTime * faceTargetSpeed);
    }

    [PunRPC]
    void StartIceBall() { if (!isAttacking) StartCoroutine(SpawnIceBall()); }

    IEnumerator SpawnIceBall() {
        isAttacking = iceBallShooting = true;
        agent.isStopped = true;
        agent.SetDestination(transform.position);
        anim.SetTrigger("IceBall");
        EnemyManager.Instance.AddAttackEnemy(enemyLimiter, id);
        yield return new WaitForSeconds(iceBallTimer);
        isAttacking = false;
        EnemyManager.Instance.RemoveAttackEnemy(enemyLimiter, id);
    }

    public void IceBallCreation() {
        if (PhotonNetwork.InRoom && photonView.IsMine)
            PhotonNetwork.Instantiate("Enemy/" + projectile.name, shootPos.transform.position, shootPos.transform.rotation);
        else if (!PhotonNetwork.InRoom)
            Instantiate(projectile, shootPos.transform.position, shootPos.transform.rotation);
        iceBallShooting = agent.isStopped = false;
    }

    [PunRPC]
    void StartSwing() { if (!isAttacking) StartCoroutine(Swing()); }

    IEnumerator Swing() {
        isAttacking = true;
        anim.SetTrigger("Attack");
        EnemyManager.Instance.AddAttackEnemy(enemyLimiter, id);
        yield return new WaitForSeconds(attackSpeed);
        isAttacking = false;
    }

    public void WeaponColliderOn() { 
        foreach (GameObject weapon in meleeWeapons)
            weapon.GetComponent<Collider>().enabled = true; 
    }

    public void WeaponColliderOff() {
        foreach (GameObject weapon in meleeWeapons) {
            weapon.GetComponent<Collider>().enabled = false;
            EnemyManager.Instance.RemoveAttackEnemy(enemyLimiter, id);
            weapon.GetComponent<WeaponController>().didDamage = false;
        }
    }

    [PunRPC]
    public void RpcTakeDamage(float damage) {
        hp -= damage;
        if (!isDOT) {
            enemyTargetPosition = FindClosestPlayer().transform.position;
            agent.SetDestination(enemyTargetPosition);
        }

        if (hp > 0)
            StartCoroutine(FlashDamage());

        if (hp <= 0 && !wasKilled) {
            GameManager.instance.updateEnemy(-1);
            EnemyManager.Instance.UpdateKillCounter(enemyLimiter);
            gameObject.GetComponent<Collider>().enabled = false;
            wasKilled = true;
            if (PhotonNetwork.InRoom)
                photonView.RPC(nameof(StartDeath), RpcTarget.All);
            else if (!PhotonNetwork.IsConnected)
                StartDeath();
        }
    }

    public void TakeDamage(float damage) { 
        if (PhotonNetwork.InRoom)
            photonView.RPC(nameof(RpcTakeDamage), RpcTarget.All, damage);
        else if (!PhotonNetwork.IsConnected)
            RpcTakeDamage(damage); 
    }

    public void Afflict(DamageStats type) {
        status = type;
        if (!isDOT)
            StartCoroutine(DamageOverTime());
    }

    IEnumerator DamageOverTime() {
        isDOT = true;
        for (int i = 0; i < status.length; i++) {
            TakeDamage(status.damage);
            yield return new WaitForSeconds(1);
        }
        isDOT = false;
    }

    IEnumerator FlashDamage() {
        model.material.color = Color.red;
        yield return new WaitForSeconds(0.1f);
        model.material.color = Color.white;
    }

    [PunRPC]
    void StartDeath() { StartCoroutine(DeathAnimation()); }

    [PunRPC]
    void SetRenderModeTransparent() { RenderModeAdjuster.SetTransparent(mat); }

    [PunRPC]
    void SetRenderModeOpaque() { RenderModeAdjuster.SetOpaque(mat); }

    IEnumerator DeathAnimation() {
        if (PhotonNetwork.InRoom)
            photonView.RPC(nameof(SetRenderModeTransparent), RpcTarget.All);
        else if (!PhotonNetwork.IsConnected)
            SetRenderModeTransparent();
        agent.isStopped = true;
        enemyTargetPosition = transform.position;
        agent.SetDestination(enemyTargetPosition);
        agent.radius = 0;
        Collider[] colliders = GetComponentsInChildren<Collider>();
        foreach (Collider collider in colliders)
            collider.enabled = false;
        anim.SetTrigger("Death");
        var renderers = new List<Renderer>();
        Renderer[] childRenders = transform.GetComponentsInChildren<Renderer>();
        renderers.AddRange(childRenders);
        yield return new WaitForSeconds(anim.GetCurrentAnimatorStateInfo(0).length);
        while (model.material.color.a > 0) {
            foreach (Renderer render in renderers) {
                if (render.material.HasProperty("_Color")) {
                    float fadeSpeed = render.material.color.a - Time.deltaTime;
                    render.material.color = new Color(render.material.color.r, render.material.color.g, render.material.color.b, fadeSpeed);
                }
                yield return null;
            }
        }

        if (PhotonNetwork.InRoom)
            photonView.RPC(nameof(SetRenderModeOpaque), RpcTarget.All);
        else if (!PhotonNetwork.IsConnected)
            SetRenderModeOpaque();

        if (PhotonNetwork.InRoom && GetComponent<PhotonView>().IsMine)
            PhotonNetwork.Destroy(gameObject);
        else if (!PhotonNetwork.InRoom)
            Destroy(gameObject);
    }

    public void OnPhotonSerializeView(PhotonStream stream, PhotonMessageInfo info) {
        if (stream.IsWriting) {
            stream.SendNext(transform.position);
            stream.SendNext(transform.rotation);
            stream.SendNext(isAttacking);
            stream.SendNext(iceBallShooting);
            stream.SendNext(curTime);
        }
        else if (stream.IsReading) {
            netPos = (Vector3)stream.ReceiveNext();
            netRot = (Quaternion)stream.ReceiveNext();
            isAttacking = (bool)stream.ReceiveNext();
            iceBallShooting = (bool)stream.ReceiveNext();
            curTime = (float)stream.ReceiveNext();

            if (isAttacking && agent.remainingDistance < swingRadius)
                photonView.RPC(nameof(StartSwing), RpcTarget.All);
            else if (iceBallShooting)
                photonView.RPC(nameof(StartIceBall), RpcTarget.All);
        }
    }
}