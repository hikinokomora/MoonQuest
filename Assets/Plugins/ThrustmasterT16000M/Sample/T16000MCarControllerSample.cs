using System;
using System.Collections.Generic;
using ThrustmasterT16000M;
using UnityEngine;

public class T16000MCarControllerSample
    : MonoBehaviour
{
    [SerializeField] private List<AxleInfo> axleInfos;
    [SerializeField] private float maxMotorTorque;
    [SerializeField] private float maxSteeringAngle;

    public void FixedUpdate()
    {
        if (!T16000MInput.IsConnected)
            return;

        // Ползунок тяги — газ,
        // наклон рукоятки на себя — тормоз.
        var speed = T16000MInput.Throttle;
        if (T16000MInput.StickY < 0)
        {
            speed = T16000MInput.StickY;
        }

        var motor = maxMotorTorque * speed;
        var steering =
            maxSteeringAngle * T16000MInput.StickX;

        foreach (var axle in axleInfos)
        {
            if (axle.steering)
            {
                axle.leftWheel.steerAngle = steering;
                axle.rightWheel.steerAngle = steering;
            }

            if (axle.motor)
            {
                axle.leftWheel.motorTorque = motor;
                axle.rightWheel.motorTorque = motor;
            }
        }
    }

    [Serializable]
    public class AxleInfo
    {
        public WheelCollider leftWheel;
        public WheelCollider rightWheel;
        public bool motor;
        public bool steering;
    }
}
