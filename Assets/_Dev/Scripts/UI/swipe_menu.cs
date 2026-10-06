using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem; // Required for the new Input System

public class swipe_menu : MonoBehaviour
{
    public GameObject scrollbar; //[cite: 2]
    float scroll_pos = 0; //[cite: 2]
    float[] pos; //[cite: 2]

    // Use this for initialization
    void Start()
    {
    }

    // Update is called once per frame
    void Update()
    {
        pos = new float[transform.childCount]; //[cite: 2]
        float distance = 1f / (pos.Length - 1f); //[cite: 2]
        for (int i = 0; i < pos.Length; i++)
        { //[cite: 2]
            pos[i] = distance * i; //[cite: 2]
        }

        // --- NEW INPUT SYSTEM CONVERSION ---
        // Replaces: if (Input.GetMouseButton(0))
        if (Pointer.current != null && Pointer.current.press.isPressed)
        {
            scroll_pos = scrollbar.GetComponent<Scrollbar>().value; //[cite: 2]
        }
        else
        {
            for (int i = 0; i < pos.Length; i++)
            { //[cite: 2]
                if (scroll_pos < pos[i] + (distance / 2) && scroll_pos > pos[i] - (distance / 2))
                { //[cite: 2]
                    scrollbar.GetComponent<Scrollbar>().value = Mathf.Lerp(scrollbar.GetComponent<Scrollbar>().value, pos[i], 0.1f); //[cite: 2]
                }
            }
        }

        for (int i = 0; i < pos.Length; i++)
        { //[cite: 2]
            if (scroll_pos < pos[i] + (distance / 2) && scroll_pos > pos[i] - (distance / 2))
            { //[cite: 2]
                // Enlarges the focused item
                transform.GetChild(i).localScale = Vector2.Lerp(transform.GetChild(i).localScale, new Vector2(1f, 1f), 0.1f); //[cite: 2]

                // Shrinks the non-focused items
                for (int a = 0; a < pos.Length; a++)
                { //[cite: 2]
                    if (a != i)
                    { //[cite: 2]
                        transform.GetChild(a).localScale = Vector2.Lerp(transform.GetChild(a).localScale, new Vector2(0.8f, 0.8f), 0.1f); //[cite: 2]
                    }
                }
            }
        }
    }
}