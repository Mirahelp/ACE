using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using AgentCommandEnvironment.Core.Enums;

namespace AgentCommandEnvironment.Core.Models;

public sealed class SuccessHeuristicItem : INotifyPropertyChanged
{
    private String? description;
    private Boolean mandatory = true;
    private String? evidence;
    private SuccessHeuristicEvaluationStatusOptions evaluationStatus = SuccessHeuristicEvaluationStatusOptions.Pending;
    private String evaluationNotes = "Pending evaluation.";

    [JsonPropertyName("description")]
    public String? Description
    {
        get { return description; }
        set
        {
            if (!String.Equals(description, value, StringComparison.Ordinal))
            {
                description = value;
                OnPropertyChanged();
            }
        }
    }

    [JsonPropertyName("mandatory")]
    public Boolean Mandatory
    {
        get { return mandatory; }
        set
        {
            if (mandatory != value)
            {
                mandatory = value;
                OnPropertyChanged();
            }
        }
    }

    [JsonPropertyName("evidence")]
    public String? Evidence
    {
        get { return evidence; }
        set
        {
            if (!String.Equals(evidence, value, StringComparison.Ordinal))
            {
                evidence = value;
                OnPropertyChanged();
            }
        }
    }

    [JsonIgnore]
    public SuccessHeuristicEvaluationStatusOptions EvaluationStatus
    {
        get { return evaluationStatus; }
        private set
        {
            if (evaluationStatus != value)
            {
                evaluationStatus = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(EvaluationGlyph));
                OnPropertyChanged(nameof(EvaluationStatusDisplay));
            }
        }
    }

    [JsonIgnore]
    public String EvaluationNotes
    {
        get { return evaluationNotes; }
        private set
        {
            if (!String.Equals(evaluationNotes, value, StringComparison.Ordinal))
            {
                evaluationNotes = value;
                OnPropertyChanged();
            }
        }
    }

    [JsonIgnore]
    public String EvaluationGlyph
    {
        get
        {
            return evaluationStatus switch
            {
                SuccessHeuristicEvaluationStatusOptions.Passed => "\u2714",
                SuccessHeuristicEvaluationStatusOptions.Failed => "\u2716",
                _ => "\u2026"
            };
        }
    }

    [JsonIgnore]
    public String EvaluationStatusDisplay
    {
        get
        {
            return evaluationStatus switch
            {
                SuccessHeuristicEvaluationStatusOptions.Passed => "Passed",
                SuccessHeuristicEvaluationStatusOptions.Failed => "Failed",
                _ => "Pending"
            };
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public void SetEvaluationResult(SuccessHeuristicEvaluationStatusOptions status, String? notes)
    {
        EvaluationStatus = status;
        String normalizedNotes;
        if (String.IsNullOrWhiteSpace(notes))
        {
            normalizedNotes = status switch
            {
                SuccessHeuristicEvaluationStatusOptions.Passed => "Met",
                SuccessHeuristicEvaluationStatusOptions.Failed => "Not met",
                _ => "Pending evaluation"
            };
        }
        else
        {
            normalizedNotes = notes!.Trim();
        }

        EvaluationNotes = normalizedNotes;
    }

    private void OnPropertyChanged([CallerMemberName] String? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}


