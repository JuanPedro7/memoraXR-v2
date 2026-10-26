using System;
using UnityEngine;

namespace SocraticTutor.VR
{
    /// <summary>
    /// Translates Meta Quest Touch controller inputs and 6DoF tracking coordinates into spatial interaction rays.
    /// Manages raycasting against spatial UI canvases, gesture confirmations, and wrist-anchored interface poses.
    /// </summary>
    public sealed class XRInteractionInput
    {
        private readonly Transform _trackingSpace;
        private readonly OVRInput.Controller _dominantController;

        public XRInteractionInput(Transform trackingSpace, OVRInput.Controller dominantController = OVRInput.Controller.RTouch)
        {
            _trackingSpace = trackingSpace ?? throw new ArgumentNullException(nameof(trackingSpace));
            _dominantController = dominantController;
        }

        /// <summary>
        /// Generates a world-space pointing ray mapped from the dominant controller's tracking pose.
        /// Converts local tracking-space poses into global scene coordinates.
        /// </summary>
        public Ray GetAimRay()
        {
            var localPosition = OVRInput.GetLocalControllerPosition(_dominantController);
            var localRotation = OVRInput.GetLocalControllerRotation(_dominantController);

            var worldPosition = _trackingSpace.TransformPoint(localPosition);
            var worldRotation = _trackingSpace.rotation * localRotation;

            return new Ray(worldPosition, worldRotation * Vector3.forward);
        }

        /// <summary>
        /// Returns true on the frame the primary index trigger is depressed.
        /// </summary>
        public bool IsConfirmPressed() => OVRInput.GetDown(OVRInput.Button.PrimaryIndexTrigger, _dominantController);

        /// <summary>
        /// Returns true on secondary face button press (typically B/Y).
        /// </summary>
        public bool IsCancelPressed() => OVRInput.GetDown(OVRInput.Button.Two, _dominantController);

        /// <summary>
        /// Returns true on hand grip trigger press for physical grab/drag actions.
        /// </summary>
        public bool IsGripPressed() => OVRInput.GetDown(OVRInput.Button.PrimaryHandTrigger, _dominantController);

        /// <summary>
        /// Retrieves the world-space pose of the non-dominant controller for glanceable wrist HUD attachments.
        /// </summary>
        public bool TryGetWristPose(out Vector3 position, out Quaternion rotation)
        {
            var nonDominantController = _dominantController == OVRInput.Controller.RTouch
                ? OVRInput.Controller.LTouch
                : OVRInput.Controller.RTouch;

            var localPosition = OVRInput.GetLocalControllerPosition(nonDominantController);
            var localRotation = OVRInput.GetLocalControllerRotation(nonDominantController);

            position = _trackingSpace.TransformPoint(localPosition);
            rotation = _trackingSpace.rotation * localRotation;
            return true;
        }
    }
}
