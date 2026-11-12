using UnityEngine;
using System.Collections.Generic;
using System.IO;

public class AddressBookManager : MonoBehaviour
{
    private static AddressBookManager _instance;
    public static AddressBookManager Instance
    {
        get
        {
            if (_instance == null)
            {
                GameObject obj = new GameObject("AddressBookManager");
                _instance = obj.AddComponent<AddressBookManager>();
                DontDestroyOnLoad(obj);
            }
            return _instance;
        }
    }

    private AddressBookData bookData;
    private string savePath;

    void Awake()
    {
        if (_instance == null)
        {
            _instance = this;
            DontDestroyOnLoad(gameObject);
            savePath = Path.Combine(Application.persistentDataPath, "address_book.json");
            LoadBook();
        }
        else if (_instance != this)
        {
            Destroy(gameObject);
        }
    }

    void LoadBook()
    {
        try
        {
            if (File.Exists(savePath))
            {
                string json = File.ReadAllText(savePath);
                bookData = JsonUtility.FromJson<AddressBookData>(json);
            }
        }
        catch (System.Exception ex) { Debug.LogError("AddressBook load error: " + ex.Message); }

        if (bookData == null)
            bookData = new AddressBookData();
        if (bookData.entries == null)
            bookData.entries = new List<AddressBookEntry>();
    }

    void SaveBook()
    {
        try
        {
            string json = JsonUtility.ToJson(bookData, true);
            File.WriteAllText(savePath, json);
        }
        catch (System.Exception ex) { Debug.LogError("AddressBook save error: " + ex.Message); }
    }

    public void AddEntry(string name, string address)
    {
        if (string.IsNullOrEmpty(address)) return;

        // Check for duplicate address
        foreach (var entry in bookData.entries)
        {
            if (entry.address == address)
            {
                entry.name = name; // Update name if address exists
                SaveBook();
                return;
            }
        }

        bookData.entries.Add(new AddressBookEntry
        {
            name = string.IsNullOrEmpty(name) ? "Unnamed" : name,
            address = address,
            dateAdded = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm")
        });
        SaveBook();
    }

    public void DeleteEntry(string address)
    {
        bookData.entries.RemoveAll(e => e.address == address);
        SaveBook();
    }

    public List<AddressBookEntry> GetEntries()
    {
        return bookData.entries ?? new List<AddressBookEntry>();
    }
}

[System.Serializable]
public class AddressBookData
{
    public List<AddressBookEntry> entries = new List<AddressBookEntry>();
}

[System.Serializable]
public class AddressBookEntry
{
    public string name;
    public string address;
    public string dateAdded;
}
