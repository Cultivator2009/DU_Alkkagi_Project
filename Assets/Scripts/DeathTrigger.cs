using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class DeathTrigger : MonoBehaviour
{
    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }
    void OnTriggerEnter(Collider other) {

		if (other.transform.tag == "GamePiece_GO") {

			// Destroy(other.gameObject);
            // other.gameObject.SetActive(false);
            Debug.Log(other+" Destroyed");

		}


	}
}
