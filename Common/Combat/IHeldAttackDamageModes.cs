namespace Vintagestory.API.Common
{
    public interface IHeldAttackDamageModes
    {
        HeldAttackDamageMode? GetDamageMode(ItemStack stack);
    }

    public class HeldAttackDamageMode(EnumDamageType damageType, string? heldRightReadyAnimation, string? heldTpHitAnimation, string[]? heldTpHitCombo, string[]? heldTpHitComboHigh, string[]? heldTpHitComboMid, string[]? heldTpHitComboLow)
    {
        public EnumDamageType DamageType { get; } = damageType;
        public string? HeldRightReadyAnimation { get; } = heldRightReadyAnimation;
        public string? HeldTpHitAnimation { get; } = heldTpHitAnimation;
        public string[]? HeldTpHitCombo { get; } = heldTpHitCombo;
        public string[]? HeldTpHitComboHigh { get; } = heldTpHitComboHigh;
        public string[]? HeldTpHitComboMid { get; } = heldTpHitComboMid;
        public string[]? HeldTpHitComboLow { get; } = heldTpHitComboLow;
    }
}
