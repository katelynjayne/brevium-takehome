using System.Text;
using System.Text.Json;

public class Appointment(int DoctorId, int PersonId, DateTime AppointmentTime, bool IsNewPatientAppointment, int RequestId)
{
    public int DoctorId { get; set; } = DoctorId;
    public int PersonId { get; set; } = PersonId;
    public DateTime AppointmentTime { get; set; } = AppointmentTime;
    public bool IsNewPatientAppointment { get; set; } = IsNewPatientAppointment;
    public int RequestId { get; set; } = RequestId;
}

public class AppointmentRequest
{
    public required int RequestId { get; set; }
    public required int PersonId { get; set; }
    public required DateTime[] PreferredDays { get; set; }
    public required int[] PreferredDocs { get; set; }
    public required bool IsNew { get; set; }
}

class Program
{
    private const string API_TOKEN = "761e6d41-d0fc-4008-abb6-cb84d6a63ff7";
    private const string BASE_URL = "https://scheduling.interviews.brevium.com/api/Scheduling";

    static async Task Main()
    {
        using HttpClient client = new HttpClient();
        try
        {
            HttpResponseMessage response = await client.PostAsync($"{BASE_URL}/Start?token={API_TOKEN}", null);
            if (!response.IsSuccessStatusCode)
            {
                throw new Exception($"{(int)response.StatusCode}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error with start: {ex.Message}");
            return;
        }

        Appointment[] appointments = [];
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };
        try
        {
            HttpResponseMessage response = await client.GetAsync($"{BASE_URL}/Schedule?token={API_TOKEN}");
            if (!response.IsSuccessStatusCode)
            {
                throw new Exception($"{(int)response.StatusCode}");
            }
            string content = await response.Content.ReadAsStringAsync() ?? throw new Exception("No content received.");
            appointments = JsonSerializer.Deserialize<Appointment[]>(content, options) ?? throw new Exception("Failed to parse.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error retrieving schedule: {ex.Message}");
            return;
        }

        Dictionary<DateOnly, List<Appointment>> appointmentDict = new Dictionary<DateOnly, List<Appointment>>();
        foreach (Appointment appointment in appointments)
        {
            DateOnly date = DateOnly.FromDateTime(appointment.AppointmentTime);
            if (appointmentDict.ContainsKey(date))
            {
                appointmentDict[date].Add(appointment);
            }
            else
            {
                appointmentDict.Add(date, new List<Appointment> { appointment });
            }
        }

        while (true)
        {
            AppointmentRequest appointmentRequest;
            try
            {
                HttpResponseMessage response = await client.GetAsync($"{BASE_URL}/AppointmentRequest?token={API_TOKEN}");
                if (!response.IsSuccessStatusCode)
                {
                    throw new Exception($"{(int)response.StatusCode}");
                }
                else if (response.StatusCode == System.Net.HttpStatusCode.NoContent)
                {
                    break;
                }
                string content = await response.Content.ReadAsStringAsync() ?? throw new Exception("No content received.");
                appointmentRequest = JsonSerializer.Deserialize<AppointmentRequest>(content, options) ?? throw new Exception("Failed to parse.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error retreiving appointment request: {ex.Message}");
                return;
            }

            foreach (DateTime dateTime in appointmentRequest.PreferredDays)
            {
                DateOnly date = DateOnly.FromDateTime(dateTime);
                int preferredTime = appointmentRequest.IsNew ? 15 : 8;
                if (!appointmentDict.ContainsKey(date))
                {
                    MakeAppointment(appointmentRequest, date, preferredTime, appointmentDict, client);
                    break;
                }
                else
                {
                    HashSet<(int hour, int doctorId)> hours = new HashSet<(int hour, int doctorId)>();
                    foreach (Appointment appt in appointmentDict[date])
                    {
                        hours.Add((appt.AppointmentTime.Hour, appt.DoctorId));
                    }
                    while (preferredTime < 17)
                    {
                        // TODO: check all doctorIds in PreferredDocs, not just the first one
                        if (!hours.Contains((preferredTime, appointmentRequest.PreferredDocs[0])))
                        {
                            MakeAppointment(appointmentRequest, date, preferredTime, appointmentDict, client);
                            break;
                        }
                        preferredTime++;
                    }
                    if (preferredTime < 17)
                    {
                        break;
                    }
                }
            }
        }
    }
    

    // TODO: need to check the constraint that appointments are scheduled a week apart per patient
    // create data structure to track appointments per patient and check before adding to schedule
    
    private async static void MakeAppointment(AppointmentRequest request, DateOnly date, int time, Dictionary<DateOnly, List<Appointment>> appointmentDict, HttpClient client)
    {
        DateTime appointmentTime = date.ToDateTime(new TimeOnly(time, 0, 0));
        Appointment appt = new(request.PreferredDocs[0], request.PersonId, appointmentTime, request.IsNew, request.RequestId);
        if (appointmentDict.ContainsKey(date))
        {
            appointmentDict[date].Add(appt);
        }
        else
        {
            appointmentDict.Add(date, new List<Appointment> { appt });
        }

        var json = JsonSerializer.Serialize(appt);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        HttpResponseMessage response = await client.PostAsync($"{BASE_URL}/Schedule?token={API_TOKEN}", content);
        if (!response.IsSuccessStatusCode)
        {
            Console.WriteLine($"Error making appointment {request.RequestId}: {response.StatusCode}");
        }
    }
}
