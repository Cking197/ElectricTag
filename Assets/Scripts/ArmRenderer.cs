using UnityEngine;

// Attach to an empty child GameObject under the player.
// Position that GameObject at the shoulder in local space.
// Assign SwordAttack and this object's Animator in the Inspector.
public class ArmRenderer : MonoBehaviour
{
    [SerializeField] private SwordAttack sword;
    [SerializeField] private Animator animator;

    private static readonly int StateArmRest = Animator.StringToHash("ArmRest");
    private static readonly int StateArmAttack = Animator.StringToHash("ArmAttack");

    private int _currentState = -1;
    private int _facingDirection;
    private PlayerController _owner;

    void Start()
    {
        _owner = GetComponentInParent<PlayerController>();
        _facingDirection = _owner != null ? _owner.FacingDirection : 1;
    }

    void LateUpdate()
    {
        if (sword == null) return;

        // Point arm tip toward the sword handle from the shoulder pivot
        Vector2 shoulder = transform.position;
        Vector2 handle = (Vector2)sword.transform.position;
        Vector2 delta = handle - shoulder;
        float angle = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg;
        if (_facingDirection == -1) angle += 180f;
        transform.rotation = Quaternion.Euler(0f, 0f, angle);

        // Swap sprite only when state changes
        int target = sword.IsAttacking ? StateArmAttack : StateArmRest;
        if (target == _currentState) return;
        _currentState = target;
        animator.Play(target);
    }
}