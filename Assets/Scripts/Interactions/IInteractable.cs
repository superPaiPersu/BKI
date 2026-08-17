using UnityEngine;

namespace CityStateSim.Interactions
{
    public interface IInteractable
    {
        string InteractionLabel { get; }
        bool CanInteract(GameObject interactor);
        void Interact(GameObject interactor);
    }
}
