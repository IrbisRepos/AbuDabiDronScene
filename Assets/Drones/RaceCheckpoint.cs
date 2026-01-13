// --------------------------------------------------
// CHECKPOINT COMPONENT (add to trigger objects)
// --------------------------------------------------
using System;
using UnityEngine;

[DisallowMultipleComponent]
public class RaceCheckpoint : MonoBehaviour
{
    [Tooltip("Порядковый номер чекпоинта. Маршрут проходится строго 0,1,2...")]
    public int Order = 0;

    [Tooltip("Если true — этот чекпоинт считается финишем.")]
    public bool isFinish = false;

    [NonSerialized] public CheckpointRace manager;

    private void Reset()
    {
        // сделаем collider trigger-ом, если он есть
        var col = GetComponent<Collider>();
        if (col != null) col.isTrigger = true;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (manager != null)
            manager.NotifyCheckpointTriggered(this, other);
    }

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        Gizmos.color = isFinish ? Color.green : Color.yellow;
        Gizmos.DrawWireSphere(transform.position, 0.5f);
    }
#endif
}