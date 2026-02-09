namespace InfernalHierarchy.Tools.Dynamic;

public interface ICustomToolSecurityPolicy
{
    CustomToolPolicyDecision Evaluate(string sourceCode);
}
