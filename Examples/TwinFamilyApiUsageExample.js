// Example JavaScript/TypeScript usage for the Twin Family Question Azure Function

/**
 * Example 1: Ask a general family question
 */
async function askFamilyQuestion() {
    try {
        const response = await fetch('/api/twin-family/ask', {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json'
            },
            body: JSON.stringify({
                twinId: '388a31e7-d408-40f0-844c-4d2efedaa836',
                language: 'English',
                question: 'Who is Daniel in the family and what is his relationship?'
            })
        });

        const result = await response.json();
        
        if (result.success) {
            console.log('Family Analysis:', result.familyAnalysis);
            document.getElementById('family-result').innerText = result.familyAnalysis;
        } else {
            console.error('Error:', result.errorMessage);
        }
    } catch (error) {
        console.error('Network error:', error);
    }
}

/**
 * Example 2: Ask about parents in Spanish
 */
async function askAboutParentsInSpanish() {
    try {
        const response = await fetch('/api/twin-family/ask', {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json'
            },
            body: JSON.stringify({
                twinId: '388a31e7-d408-40f0-844c-4d2efedaa836',
                language: 'Spanish',
                question: '¿Quiénes son los padres en esta familia y qué ocupación tienen?'
            })
        });

        const result = await response.json();
        
        if (result.success) {
            console.log('Análisis Familiar:', result.familyAnalysis);
            document.getElementById('family-result-spanish').innerText = result.familyAnalysis;
        } else {
            console.error('Error:', result.errorMessage);
        }
    } catch (error) {
        console.error('Network error:', error);
    }
}

/**
 * Example 3: Use the GET endpoint for simple questions
 */
async function simpleGetFamilyQuestion() {
    try {
        const twinId = '388a31e7-d408-40f0-844c-4d2efedaa836';
        const question = encodeURIComponent('Tell me about family occupations');
        const language = 'English';
        
        const response = await fetch(
            `/api/twin-family/ask/${twinId}?question=${question}&language=${language}`
        );

        const result = await response.text();
        console.log('Family Analysis:', result);
        document.getElementById('simple-family-result').innerText = result;
    } catch (error) {
        console.error('Network error:', error);
    }
}

/**
 * Example 4: Interactive family questions
 */
async function interactiveFamilyChat() {
    const questions = [
        'Who are the parents in the family?',
        'Tell me about the siblings',
        'What are the different occupations in the family?',
        'What languages do family members speak?',
        'Tell me about family interests and hobbies'
    ];

    for (const question of questions) {
        try {
            const response = await fetch('/api/twin-family/ask', {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json'
                },
                body: JSON.stringify({
                    twinId: '388a31e7-d408-40f0-844c-4d2efedaa836',
                    language: 'English',
                    question: question
                })
            });

            const result = await response.json();
            
            if (result.success) {
                console.log(`Q: ${question}`);
                console.log(`A: ${result.familyAnalysis}`);
                console.log('---');
                
                // Add to UI
                const chatDiv = document.getElementById('family-chat');
                chatDiv.innerHTML += `
                    <div class="question">Q: ${question}</div>
                    <div class="answer">A: ${result.familyAnalysis}</div>
                    <hr>
                `;
            }
        } catch (error) {
            console.error(`Error with question "${question}":`, error);
        }
        
        // Wait 1 second between questions
        await new Promise(resolve => setTimeout(resolve, 1000));
    }
}

/**
 * Example 5: Error handling demonstration
 */
async function demonstrateErrorHandling() {
    // Test with invalid Twin ID
    try {
        const response = await fetch('/api/twin-family/ask', {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json'
            },
            body: JSON.stringify({
                twinId: '', // Empty Twin ID should cause error
                language: 'English',
                question: 'Tell me about the family'
            })
        });

        const result = await response.json();
        
        if (!result.success) {
            console.log('Expected error handled:', result.errorMessage);
        }
    } catch (error) {
        console.error('Network error:', error);
    }
}

// Call examples when page loads
document.addEventListener('DOMContentLoaded', function() {
    // Uncomment the functions you want to test
    // askFamilyQuestion();
    // askAboutParentsInSpanish();
    // simpleGetFamilyQuestion();
    // interactiveFamilyChat();
    // demonstrateErrorHandling();
});