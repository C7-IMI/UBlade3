namespace UBlade3.PlanVM
{
    /// <summary>
    /// MsgInteractor's named slot (interactor.h GetNamedSlot): the message id the engine's scene
    /// handler table indexes by. The original virtual lives on Interactor and the message interactor
    /// overrides it with m_ownSlotOrMessage; this slice keeps the base file untouched and exposes the
    /// same read as an extension for the Plan AI host.
    /// </summary>
    public static class PlanAiInteractorExtensions
    {
        public static short GetNamedSlot(this PlanMessageInteractor interactor)
        {
            return unchecked((short)interactor.OwnSlotOrMessage.Value);
        }
    }
}
